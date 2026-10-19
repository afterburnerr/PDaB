"""Build physical apartment plaque meshes using installed fontTools and system GLU.

Run: python Tools/Art/build_door_plaque_meshes.py
Coordinates are X right, Y up, visible front +Z. No Blender/OBJ importer needed.
"""
import ctypes as C
import ctypes.util
import json
import math
from pathlib import Path
from fontTools.pens.basePen import BasePen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'Assets/Art/Environment/DoorPlaques/Meshes/DoorPlaqueMeshes.json'
FONT = ROOT / 'Assets/Art/UI/Entrance/Fonts/NotoSerif-Bold.ttf'


def area(points):
    return sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(points, points[1:] + points[:1])) / 2


def inset(points, distance):
    result = []
    for a, b, c in zip(points[-1:] + points[:-1], points, points[1:] + points[:1]):
        def right(p, q):
            dx, dy = q[0] - p[0], q[1] - p[1]
            length = math.hypot(dx, dy)
            return (dy / length, -dx / length)
        n1, n2 = right(a, b), right(b, c)
        nx, ny = n1[0] + n2[0], n1[1] + n2[1]
        length = math.hypot(nx, ny)
        if length < 1e-8:
            result.append(b)
            continue
        nx, ny = nx / length, ny / length
        miter = min(distance / max(0.2, nx * n1[0] + ny * n1[1]), distance * 3)
        result.append((b[0] + nx * miter, b[1] + ny * miter))
    return result


def triangulate(contours):
    lib = C.CDLL(ctypes.util.find_library('GLU'))
    lib.gluNewTess.restype = C.c_void_p
    tess = lib.gluNewTess()
    for name in ('gluTessBeginPolygon', 'gluTessEndPolygon', 'gluTessBeginContour', 'gluTessEndContour', 'gluDeleteTess'):
        getattr(lib, name).argtypes = [C.c_void_p] if name != 'gluTessBeginPolygon' else [C.c_void_p, C.c_void_p]
    lib.gluTessVertex.argtypes = [C.c_void_p, C.POINTER(C.c_double), C.c_void_p]
    lib.gluTessCallback.argtypes = [C.c_void_p, C.c_uint, C.c_void_p]
    lib.gluTessNormal.argtypes = [C.c_void_p, C.c_double, C.c_double, C.c_double]
    lib.gluTessNormal(tess, 0, 0, 1)
    points = [p for contour in contours for p in contour]
    storage = [(C.c_double * 3)(x, y, 0) for x, y in points]
    result, primitive, mode, errors = [], [], [0], []
    def begin(value):
        mode[0] = value
        primitive.clear()
    def vertex(pointer):
        primitive.append(int(pointer) - 1)
    def end():
        if mode[0] == 4:
            result.extend(tuple(primitive[i:i+3]) for i in range(0, len(primitive), 3))
        elif mode[0] == 6:
            result.extend((primitive[0], primitive[i], primitive[i+1]) for i in range(1, len(primitive)-1))
        elif mode[0] == 5:
            result.extend((primitive[i], primitive[i+1], primitive[i+2]) if i % 2 == 0 else (primitive[i+1], primitive[i], primitive[i+2]) for i in range(len(primitive)-2))
    callbacks = [C.CFUNCTYPE(None, C.c_uint)(begin), C.CFUNCTYPE(None, C.c_void_p)(vertex), C.CFUNCTYPE(None)(end), C.CFUNCTYPE(None, C.c_uint)(errors.append)]
    for constant, callback in zip((100100, 100101, 100102, 100103), callbacks):
        lib.gluTessCallback(tess, constant, C.cast(callback, C.c_void_p))
    lib.gluTessBeginPolygon(tess, None)
    index = 0
    for contour in contours:
        lib.gluTessBeginContour(tess)
        for unused in contour:
            lib.gluTessVertex(tess, storage[index], C.c_void_p(index+1))
            index += 1
        lib.gluTessEndContour(tess)
    lib.gluTessEndPolygon(tess)
    lib.gluDeleteTess(tess)
    if errors:
        raise ValueError(f'GLU tessellation errors {errors}')
    return points, result


class Mesh:
    def __init__(self, name):
        self.name, self.vertices, self.normals, self.triangles = name, [], [], []
    def triangle(self, a, b, c):
        ab, ac = [b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)]
        n = (ab[1]*ac[2]-ab[2]*ac[1], ab[2]*ac[0]-ab[0]*ac[2], ab[0]*ac[1]-ab[1]*ac[0])
        length = math.sqrt(sum(v*v for v in n))
        if length < 1e-14:
            return
        start = len(self.vertices)
        self.vertices.extend((a, b, c))
        self.normals.extend([tuple(v/length for v in n)] * 3)
        self.triangles.extend((start, start+1, start+2))
    def cap(self, contours, z, forward):
        points, triangles = triangulate(contours)
        for triangle in triangles:
            a, b, c = [(*points[i], z) for i in triangle]
            positive = (b[0]-a[0])*(c[1]-a[1]) - (b[1]-a[1])*(c[0]-a[0]) > 0
            self.triangle(a, b, c) if positive == forward else self.triangle(a, c, b)
    def connect(self, lower, upper, z1, z2):
        # Clockwise outer contours and counterclockwise holes face away from solid.
        for i, a in enumerate(lower):
            j = (i+1) % len(lower)
            b, c, d = lower[j], upper[j], upper[i]
            self.triangle((*a,z1), (*c,z2), (*b,z1))
            self.triangle((*a,z1), (*d,z2), (*c,z2))
    def export(self):
        minimum = [min(v[i] for v in self.vertices) for i in range(3)]
        maximum = [max(v[i] for v in self.vertices) for i in range(3)]
        size = [maximum[i]-minimum[i] for i in range(3)]
        def vector(v):
            return {axis: round(float(n), 8) for axis,n in zip('xyz',v)}
        return {'name':self.name, 'vertices':[vector(v) for v in self.vertices], 'normals':[vector(v) for v in self.normals],
                'uv':[{'x':round((v[0]-minimum[0])/size[0],8), 'y':round((v[1]-minimum[1])/size[1],8)} for v in self.vertices],
                'triangles':self.triangles, 'width':size[0], 'height':size[1], 'depth':size[2]}


def rounded(width, height, radius):
    points=[]
    for cx,cy,start in ((width/2-radius,height/2-radius,0),(-width/2+radius,height/2-radius,90),(-width/2+radius,-height/2+radius,180),(width/2-radius,-height/2+radius,270)):
        for step in range(7):
            angle=math.radians(start+90*step/6)
            points.append((cx+math.cos(angle)*radius,cy+math.sin(angle)*radius))
    return list(reversed(points))


def plaque():
    mesh=Mesh('Plaque')
    rings=[(rounded(.1342,.0652,.0086),0),(rounded(.135,.066,.009),.0004),(rounded(.135,.066,.009),.002),(rounded(.134,.065,.0085),.0025)]
    mesh.cap([rings[0][0]],0,False)
    for (a,z1),(b,z2) in zip(rings,rings[1:]):
        mesh.connect(a,b,z1,z2)
    mesh.cap([rings[-1][0]],.0025,True)
    return mesh


def screw():
    mesh=Mesh('Screw')
    circle=[(.0032*math.cos(-i*math.tau/20),.0032*math.sin(-i*math.tau/20)) for i in range(20)]
    mesh.cap([circle],0,False)
    mesh.connect(circle,circle,0,.00045)
    mesh.cap([circle],.00045,True)
    angle=math.asin(.0004/.0032)
    # Two raised circular segments leave a real 0.8mm screwdriver slot.
    for sign in (1,-1):
        contour=[(sign*.0032*math.cos(angle+(math.pi-2*angle)*i/12),sign*.0032*math.sin(angle+(math.pi-2*angle)*i/12)) for i in range(13)]
        if area(contour)>0: contour.reverse()
        top=inset(contour,.00025)
        mesh.connect(contour,contour,.00045,.00115)
        mesh.connect(contour,top,.00115,.0016)
        mesh.cap([top],.0016,True)
    return mesh


class FlattenPen(BasePen):
    def __init__(self, glyphs):
        super().__init__(glyphs)
        self.contours=[]
    def _moveTo(self,p): self.contours.append([p])
    def _lineTo(self,p): self.contours[-1].append(p)
    def _qCurveToOne(self,p1,p2):
        p0=self._getCurrentPoint()
        for i in range(1,7):
            t=i/6; s=1-t
            self.contours[-1].append((s*s*p0[0]+2*s*t*p1[0]+t*t*p2[0],s*s*p0[1]+2*s*t*p1[1]+t*t*p2[1]))
    def _curveToOne(self,p1,p2,p3):
        p0=self._getCurrentPoint()
        for i in range(1,9):
            t=i/8; s=1-t
            self.contours[-1].append(tuple(s**3*p0[a]+3*s*s*t*p1[a]+3*s*t*t*p2[a]+t**3*p3[a] for a in (0,1)))
    def _closePath(self):
        points=self.contours[-1]
        if math.dist(points[0],points[-1])<1e-7: points.pop()
    def _endPath(self): self._closePath()


def digit(character,glyphs,cmap):
    pen=FlattenPen(glyphs)
    glyphs[cmap[ord(character)]].draw(pen)
    contours=pen.contours
    points=[p for c in contours for p in c]
    minx,maxx=min(p[0] for p in points),max(p[0] for p in points)
    miny,maxy=min(p[1] for p in points),max(p[1] for p in points)
    height=maxy-miny
    contours=[[((x-(minx+maxx)/2)/height,(y-(miny+maxy)/2)/height) for x,y in c] for c in contours]
    if area(max(contours,key=lambda c:abs(area(c))))>0: contours=[list(reversed(c)) for c in contours]
    mesh=Mesh('Digit_'+character)
    mesh.cap(contours,0,False)
    upper=[inset(c,.003) for c in contours]
    for a,b in zip(contours,upper):
        mesh.connect(a,a,0,.016)
        mesh.connect(a,b,.016,.02)
    mesh.cap(upper,.02,True)
    return mesh


def main():
    font=TTFont(FONT)
    glyphs=font.getGlyphSet()
    meshes=[plaque(),screw()]+[digit(str(i),glyphs,font.getBestCmap()) for i in range(10)]
    data={'formatVersion':1,'coordinates':'X right, Y up, visible front +Z; back Z=0. Preserve triangle indices; cross products/normals point outward.',
          'units':'Plaque/Screw metres. Digits height=1, depth=.02; uniform runtime scale equals desired digit height in metres.',
          'font':'NotoSerif-Bold.ttf (SIL Open Font License), existing project font', 'meshes':[m.export() for m in meshes]}
    OUTPUT.parent.mkdir(parents=True,exist_ok=True)
    OUTPUT.write_text(json.dumps(data,separators=(',',':'))+'\n')
    for m in data['meshes']: print(m['name'],len(m['triangles'])//3,'triangles',m['width'],m['height'],m['depth'])
    print('Wrote',OUTPUT)


if __name__=='__main__': main()
