"""Build the original GoldSrc-inspired Soviet worker. Run inside Blender via MCP.
No external packages. Only the SovietWorker collection is replaced on rerun.
Textures: Assets/Characters/SovietWorker/Textures/Source/{jacket,trousers,face,skin,hair,boots,shirt}.png.
Call build_worker(); export_worker(); render_preview() separately.
"""
import bpy
import bmesh
import math
import os
from mathutils import Vector

ROOT = '/home/afterburner/Projects/unity/FPLab'
SOURCE = ROOT + '/Tools/Art/SovietWorker'
OUTPUT = ROOT + '/Assets/Characters/SovietWorker'
INPUT = OUTPUT + '/Textures/Source'
COLLECTION = 'SovietWorker'
# UV coordinates use Blender's bottom-left origin; source images are copied upright.
REGIONS = {
    'jacket': (0, .5, .5, 1), 'trousers': (.5, 1, .5, 1),
    'face': (0, .5, 0, .5), 'skin': (.5, .75, .25, .5),
    'hair': (.75, 1, .25, .5), 'boots': (.5, .75, 0, .25),
    'shirt': (.75, 1, 0, .25),
}
PALETTES = {'jacket': (.24,.29,.28), 'trousers': (.17,.18,.18),
            'face': (.57,.44,.35), 'skin': (.57,.44,.35),
            'hair': (.17,.16,.14), 'boots': (.075,.069,.061), 'shirt': (.63,.61,.53)}

def face_uv(p):
    x,y,z=p
    landmarks=[(1.515,.015),(1.535,.075),(1.597,.255),(1.627,.385),(1.654,.56),(1.672,.635),(1.704,.82),(1.762,1.0)]
    v=landmarks[0][1]
    for (za,va),(zb,vb) in zip(landmarks,landmarks[1:]):
        if za<=z<=zb:
            v=va+(vb-va)*(z-za)/(zb-za); break
    return ((x+.079)/.158,v)

class Mesh:
    def __init__(self):
        self.v=[]; self.faces=[]; self.uv=[]; self.weights=[]
    def vert(self, p, weights):
        self.v.append(tuple(p)); self.weights.append(weights); return len(self.v)-1
    def face(self, indices, region, uv=None):
        self.faces.append(tuple(indices))
        if uv is None:
            uv=[(0,0),(1,0),(1,1),(0,1)][:len(indices)]
        u0,u1,v0,v1=REGIONS[region]
        # An inset protects UVs against adjacent atlas islands / mip bleed.
        pad=2/1024
        self.uv.append([(u0+pad+u*(u1-u0-2*pad),v0+pad+v*(v1-v0-2*pad)) for u,v in uv])
    def bridge(self, a,b, region, ta=0,tb=1, reverse=False):
        n=len(a)
        for i in range(n):
            j=(i+1)%n
            ids=[a[i],a[j],b[j],b[i]]
            uv=[(i/n,ta),((i+1)/n,ta),((i+1)/n,tb),(i/n,tb)]
            if reverse: ids.reverse(); uv.reverse()
            self.face(ids,region,uv)
    def ring(self,center,rx,ry,n,weights,axis=None):
        c=Vector(center)
        if axis is None: e1=Vector((1,0,0)); e2=Vector((0,1,0))
        else:
            d=Vector(axis).normalized(); e1=Vector((0,1,0)); e2=d.cross(e1).normalized()
        return [self.vert(c+e1*(math.sin(i*math.tau/n)*rx)+e2*(math.cos(i*math.tau/n)*ry),weights) for i in range(n)]
    def box(self,c,s,region,weights):
        x,y,z=c; a,b,d=[q/2 for q in s]
        ids=[self.vert((x+dx*a,y+dy*b,z+dz*d),weights) for dx,dy,dz in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
        for f in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]: self.face([ids[i] for i in f],region)
    def patch(self,points,region,weights,uv=None):
        self.face([self.vert(p,weights) for p in points],region,uv)
    def finish(self, collection):
        data=bpy.data.meshes.new('SovietWorker_EditableTopology'); data.from_pydata(self.v,[],self.faces); data.update()
        obj=bpy.data.objects.new('SovietWorker_Body',data); collection.objects.link(obj)
        layer=data.uv_layers.new(name='WorkerAtlasUV')
        for poly,uvs in zip(data.polygons,self.uv):
            for li,uv in zip(poly.loop_indices,uvs): layer.data[li].uv=uv
            poly.use_smooth=True
        for weights in self.weights:
            for name in weights:
                if obj.vertex_groups.get(name) is None: obj.vertex_groups.new(name=name)
        for i,weights in enumerate(self.weights):
            total=sum(weights.values())
            for name,w in weights.items(): obj.vertex_groups[name].add([i],w/total,'REPLACE')
        # Correct outward face orientation without triangulating the editable source.
        bm=bmesh.new(); bm.from_mesh(data); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(data); bm.free(); data.update()
        bpy.context.view_layer.objects.active=obj; obj.select_set(True)
        return obj

def atlas():
    os.makedirs(OUTPUT+'/Textures',exist_ok=True)
    existing=bpy.data.images.get('SovietWorker_Diffuse')
    if existing: bpy.data.images.remove(existing)
    image=bpy.data.images.new('SovietWorker_Diffuse',width=1024,height=1024,alpha=False)
    pixels=[0.0]*(1024*1024*4)
    missing=[]
    for key,region in REGIONS.items():
        u0,u1,v0,v1=region; x0=int(u0*1024); y0=int(v0*1024); w=int((u1-u0)*1024); h=int((v1-v0)*1024)
        path=INPUT+'/'+key+'.png'
        source=None
        if os.path.exists(path):
            source=bpy.data.images.load(path,check_existing=False); source.scale(w,h); sp=list(source.pixels)
        else: missing.append(key)
        for y in range(h):
            for x in range(w):
                dest=((y0+y)*1024+x0+x)*4
                if source:
                    src=(y*w+x)*4; pixels[dest:dest+4]=sp[src:src+3]+[1]
                else:
                    # Deterministic fallback permits geometry inspection before art arrives.
                    noise=(((x*73856093)^(y*19349663))%101/100-.5)*.026
                    pixels[dest:dest+4]=[max(0,min(1,c+noise)) for c in PALETTES[key]]+[1]
        if source: bpy.data.images.remove(source)
    image.pixels.foreach_set(pixels); image.filepath_raw=OUTPUT+'/Textures/WorkerDiffuse.png'; image.file_format='PNG'; image.save(); image.pack()
    mat=bpy.data.materials.get('SovietWorker_Atlas') or bpy.data.materials.new('SovietWorker_Atlas')
    mat.use_nodes=True; nodes=mat.node_tree.nodes; nodes.clear()
    out=nodes.new('ShaderNodeOutputMaterial'); bsdf=nodes.new('ShaderNodeBsdfPrincipled'); tex=nodes.new('ShaderNodeTexImage'); tex.image=image; tex.interpolation='Linear'
    bsdf.inputs['Roughness'].default_value=.94; mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color']); mat.node_tree.links.new(bsdf.outputs['BSDF'],out.inputs['Surface'])
    return mat,missing

def build_worker():
    old=bpy.data.collections.get(COLLECTION)
    if old:
        for obj in list(old.all_objects): bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(old)
    collection=bpy.data.collections.new(COLLECTION); bpy.context.scene.collection.children.link(collection)
    bpy.ops.object.select_all(action='DESELECT')
    m=Mesh()
    # Jacket: continuous rings with real openings for the shoulder-to-arm topology.
    levels=[(.87,.175,.119),(.895,.181,.124),(1.00,.184,.135),(1.14,.197,.139),(1.28,.214,.139),(1.39,.225,.131),(1.465,.205,.116),(1.495,.112,.084)]
    rings=[]
    for z,rx,ry in levels:
        weights={'Hips':max(0,1-(z-.87)/.25),'Spine':1} if z<1.14 else {'Chest':1}
        rings.append(m.ring((0,0,z),rx,ry,16,weights))
    for k in range(len(rings)-1):
        for i in range(16):
            if k in (4,5) and (2<=i<6 or 10<=i<14): continue
            j=(i+1)%16
            # Front/back projection keeps vertical fabric grain vertical.
            ids=[rings[k][i],rings[k][j],rings[k+1][j],rings[k+1][i]]
            uv=[((m.v[q][0]+.235)/.47,(m.v[q][2]-.865)/.64) for q in ids]
            m.face(ids,'jacket',uv)
    # Inset shirt and neck rather than a solid block closing the collar.
    m.box((0,-.071,1.468),(.113,.022,.10),'shirt',{'Chest':1})
    neck=[]
    for z,rx,ry in [(1.465,.047,.042),(1.515,.048,.041),(1.545,.054,.044)]: neck.append(m.ring((0,.003,z),rx,ry,12,{'Neck':1}))
    for i in range(2): m.bridge(neck[i],neck[i+1],'skin',i/2,(i+1)/2)
    # Collar points stand proud of the jacket, with a readable open V.
    for s in (-1,1):
        m.patch([(s*.022,-.099,1.49),(s*.098,-.103,1.489),(s*.128,-.126,1.428),(s*.071,-.144,1.435)],'jacket',{'Chest':1})
        # Sewn chest pockets, flap, side seams and sleeve cuffs are geometry.
        m.box((s*.112,-.144,1.30),(.095,.011,.108),'jacket',{'Chest':1})
        m.patch([(s*.064,-.153,1.355),(s*.16,-.153,1.355),(s*.153,-.158,1.332),(s*.078,-.158,1.326)],'jacket',{'Chest':1})
        # Central placket is narrow and follows the torso, not a floating belt.
    m.box((0,-.143,1.16),(.019,.009,.43),'jacket',{'Spine':.5,'Chest':.5})
    for z in (.96,1.07,1.18,1.29,1.39):
        r=m.ring((.002,-.151,z),.004,.004,6,{'Chest':1} if z>1.2 else {'Spine':1},axis=(0,1,0)); m.face(r,'boots',[(.4,.4)]*6)
    for s,side,indices in [(1,'Left',list(range(2,7))),(-1,'Right',list(range(10,15)))]:
        # 12-edge opening shares every vertex with the jacket.
        a=indices[0]; b=indices[-1]
        opening=[rings[4][i] for i in indices]+[rings[5][b]]+[rings[6][i] for i in reversed(indices)]+[rings[5][a]]
        # Parameter orientation derived from the opening ensures no twisted seam.
        centers=[(s*.269,0,1.373),(s*.34,0,1.282),(s*.408,0,1.188),(s*.434,0,1.15),(s*.454,-.001,1.121),(s*.509,-.002,1.034),(s*.568,-.004,.958),(s*.578,-.004,.943)]
        widths=[(.091,.089),(.082,.082),(.071,.07),(.069,.067),(.068,.065),(.061,.06),(.05,.049),(.051,.049)]
        prev=opening; base_c=Vector((s*.215,0,1.38)); axis=Vector((s*.6,0,-.8)); e1=Vector((0,1,0)); e2=axis.cross(e1).normalized()
        angles=[]
        for vi in opening:
            d=Vector(m.v[vi])-base_c; angles.append(math.atan2(d.dot(e1),d.dot(e2)))
        for k,(center,(rx,ry)) in enumerate(zip(centers,widths)):
            t=max(0,min(1,(k-2)/3))
            weights={side+'UpperArm':1-t,side+'LowerArm':t}
            if k>=6: weights={side+'LowerArm':.8,side+'Hand':.2}
            ring=[m.vert(Vector(center)+e1*(math.sin(a)*rx)+e2*(math.cos(a)*ry),weights) for a in angles]
            m.bridge(prev,ring,'jacket',k/len(centers),(k+1)/len(centers)); prev=ring
        # Visible wrist, broad palm, knuckles, tapered fingers in one connected hand.
        for k,(c,rx,ry) in enumerate([((s*.588,-.004,.929),.039,.036),((s*.614,-.007,.893),.047,.034),((s*.642,-.011,.856),.046,.029),((s*.658,-.017,.831),.037,.024),((s*.665,-.021,.818),.025,.017)]):
            ring=[m.vert(Vector(c)+e1*(math.sin(a)*rx)+e2*(math.cos(a)*ry),{side+'Hand':1}) for a in angles]
            m.bridge(prev,ring,'skin',k/5,(k+1)/5); prev=ring
        m.face(list(reversed(prev)),'skin',[(.5,.5)]*len(prev))
        # Thumb with two articulated taper loops, attached at the palm's inner side.
        thumb=[]
        for c,rx in [((s*.596,-.039,.904),.022),((s*.614,-.05,.875),.019),((s*.63,-.048,.866),.012)]: thumb.append(m.ring(c,rx,rx*.8,6,{side+'Hand':1},axis=(s*.7,-.1,-.7)))
        for j in range(2): m.bridge(thumb[j],thumb[j+1],'skin',j/2,(j+1)/2)
        m.face(thumb[-1],'skin',[(.5,.5)]*6)
    # Trousers: both legs share a pelvis and central crotch seam.
    waist=m.ring((0,0,.925),.174,.116,16,{'Hips':1})
    hip=m.ring((0,0,.815),.187,.135,16,{'Hips':1})
    m.bridge(waist,hip,'trousers',.88,1)
    crotch=m.vert((0,.0,.748),{'Hips':1})
    for s,side,arc in [(1,'Left',[i for i in range(9)]),(-1,'Right',[i%16 for i in range(8,17)])]:
        prev=[hip[i] for i in arc]+[crotch]
        sections=[(.755,.105,.103,.109),(.665,.108,.092,.098),(.545,.112,.088,.094),(.497,.115,.087,.092),(.461,.115,.088,.094),(.37,.119,.079,.088),(.25,.123,.076,.081),(.192,.126,.08,.082),(.172,.126,.078,.08)]
        # At each knee, three closely spaced rings support bending rather than a single hinge.
        for k,(z,cx,rx,ry) in enumerate(sections):
            t=max(0,min(1,(.55-z)/.1)); weights={side+'UpperLeg':1-t,side+'LowerLeg':t}
            if z>.74: weights={'Hips':.3,side+'UpperLeg':.7}
            n=len(prev)
            # Match the hip's arc orientation from front center around the outside to back center.
            ring=[]
            for i in range(n):
                a=math.pi+i*math.tau/n if s>0 else i*math.tau/n
                ring.append(m.vert((s*cx+math.sin(a)*rx,math.cos(a)*ry,z),weights))
            # Rotate/reverse by nearest first seam, preserving around-ring winding.
            if k==0:
                candidates=[]
                for rev in (False,True):
                    rr=list(reversed(ring)) if rev else ring
                    for shift in range(n):
                        q=rr[shift:]+rr[:shift]
                        cost=sum((Vector(m.v[a])-Vector(m.v[b])).length_squared for a,b in zip(prev,q))
                        candidates.append((cost,q))
                ring=min(candidates,key=lambda q:q[0])[1]
                # Save exact ring angle order for all remaining leg sections.
                leg_angles=[math.atan2((m.v[v][0]-s*cx)/rx,m.v[v][1]/ry) for v in ring]
            else:
                # Replace generated vertices with the persistent angle order (no twisting).
                for vi,a in zip(ring,leg_angles): m.v[vi]=(s*cx+math.sin(a)*rx,math.cos(a)*ry,z)
            m.bridge(prev,ring,'trousers',1-k/len(sections),1-(k+1)/len(sections)); prev=ring
        # Work boot: ankle shaft, instep, angular toe cap, separate thick sole.
        boot=[]
        for z,c_y,rx,ry in [(.225,.005,.062,.072),(.18,.003,.067,.079),(.112,-.018,.073,.114),(.064,-.049,.075,.145),(.035,-.052,.077,.148),(.012,-.052,.077,.148)]:
            boot.append(m.ring((s*.126,c_y,z),rx,ry,12,{side+'Foot':1}))
        for j in range(len(boot)-1): m.bridge(boot[j],boot[j+1],'boots',1-j/5,1-(j+1)/5)
        m.face(boot[-1],'boots',[(.5,.5)]*12)
        # Laces are strips, unintrusive at game viewing distance.
        for z,y,w in [(.105,-.076,.061),(.115,-.056,.052),(.126,-.036,.047)]: m.box((s*.126,y,z),(w,.009,.004),'trousers',{side+'Foot':1})
    # Angular adult head. Front is -Y. Rings sculpt chin, jaw, cheek, brow and crown.
    head_sections=[(1.515,.037,.041,.0),(1.535,.055,.055,-.003),(1.57,.069,.066,.0),(1.597,.074,.071,.001),(1.63,.077,.077,.003),(1.654,.076,.076,.005),(1.672,.075,.077,.007),(1.704,.072,.079,.009),(1.733,.058,.067,.012),(1.755,.028,.04,.013),(1.762,.006,.012,.013)]
    head=[]
    for z,rx,ry,cy in head_sections:
        ring=m.ring((0,cy,z),rx,ry,16,{'Head':1})
        # Recess eye sockets, flatten cheeks and set lower jaw/chin forward.
        for i,vi in enumerate(ring):
            x,y,zz=m.v[vi]
            if y<cy and z in (1.63,1.654) and .018<abs(x)<.061: y+=.008
            if y<cy and z<=1.57: y-=.006
            m.v[vi]=(x,y,zz)
        head.append(ring)
    for k in range(len(head)-1):
        for i in range(16):
            ids=[head[k][i],head[k][(i+1)%16],head[k+1][(i+1)%16],head[k+1][i]]
            front=4<=i<12
            hair=k>=8 or (not front and k>=5)
            region='hair' if hair else ('face' if front else 'skin')
            if region=='face': uv=[face_uv(m.v[q]) for q in ids]
            else: uv=[(i/16,k/10),((i+1)/16,k/10),((i+1)/16,(k+1)/10),(i/16,(k+1)/10)]
            m.face(ids,region,uv)
    m.face(head[-1],'hair',[(.5,.5)]*16)
    # Nose has an actual bridge, pointed tip, nostrils and alar planes.
    nose=[(-.012,-.083,1.666),(.012,-.083,1.666),(-.012,-.103,1.642),(.012,-.103,1.642),(-.015,-.125,1.627),(.015,-.125,1.627),(-.023,-.093,1.619),(.023,-.093,1.619),(0,-.109,1.615)]
    ni=[m.vert(p,{'Head':1}) for p in nose]
    for f in [(0,1,3,2),(2,3,5,4),(0,2,6),(1,7,3),(2,4,8,6),(3,7,8,5),(4,5,8),(6,8,7)]:
        ids=[ni[i] for i in f]; m.face(ids,'face',[face_uv(m.v[q]) for q in ids])
    # Brow ridges and mouth planes enhance the flat-lit GoldSrc silhouette.
    for s in (-1,1):
        m.patch([(s*.013,-.084,1.662),(s*.057,-.071,1.662),(s*.057,-.076,1.669),(s*.015,-.09,1.67)],'face',{'Head':1},[face_uv(p) for p in [(s*.013,-.084,1.662),(s*.057,-.071,1.662),(s*.057,-.076,1.669),(s*.015,-.09,1.67)]])
        # Ear has outer rim, inset concha, and lobule rather than a cube.
        ear=[]
        for p in [(s*.075,.013,1.66),(s*.091,.007,1.659),(s*.095,-.004,1.642),(s*.091,-.007,1.615),(s*.079,-.002,1.607),(s*.075,.005,1.626),(s*.087,-.011,1.636)]: ear.append(m.vert(p,{'Head':1}))
        for j in range(6): m.face([ear[j],ear[(j+1)%6],ear[6]],'skin',[(.3,.8),(.7,.8),(.5,.3)])
        # Hair sideburn.
        m.patch([(s*.073,-.024,1.678),(s*.079,-.019,1.676),(s*.078,-.022,1.645),(s*.072,-.029,1.65)],'hair',{'Head':1})
    m.patch([(-.027,-.073,1.597),(.027,-.073,1.597),(.022,-.079,1.602),(-.022,-.079,1.602)],'face',{'Head':1},[face_uv(p) for p in [(-.027,-.073,1.597),(.027,-.073,1.597),(.022,-.079,1.602),(-.022,-.079,1.602)]])
    obj=m.finish(collection)
    mat,missing=atlas(); obj.data.materials.append(mat)
    rig=create_rig(collection); mod=obj.modifiers.new('Humanoid skinning','ARMATURE'); mod.object=rig; obj.parent=rig
    obj['style']='Original Soviet worker, 1998 GoldSrc / Half-Life 1 inspired'; obj['height_m']=1.762
    obj['atlas_regions']=str(REGIONS); obj['texture_inputs_missing']=', '.join(missing)
    obj['authoring_notes']='Editable quad rings, welded shoulders and trouser crotch; garment, ear, thumb and boot detail islands intentional.'
    # Mark sharp boundaries on disconnected clothing details while retaining ring shading.
    bpy.context.view_layer.objects.active=obj
    scene=bpy.context.scene; scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1.0
    obj.data.calc_loop_triangles()
    result={'vertices':len(obj.data.vertices),'polygons':len(obj.data.polygons),'triangles':len(obj.data.loop_triangles),'bones':len(rig.data.bones),'missing_textures':missing}
    print('SOVIET_WORKER_BUILD',result)
    return result

def create_rig(collection):
    data=bpy.data.armatures.new('SovietWorker_Humanoid'); rig=bpy.data.objects.new('SovietWorker_Rig',data); collection.objects.link(rig)
    bpy.context.view_layer.objects.active=rig; rig.select_set(True); bpy.ops.object.mode_set(mode='EDIT')
    def bone(name,head,tail,parent=None):
        b=data.edit_bones.new(name); b.head=head; b.tail=tail
        if parent: b.parent=data.edit_bones[parent]
        return b
    bone('Root',(0,0,0),(0,0,.15)); bone('Hips',(0,0,.81),(0,0,.97),'Root'); bone('Spine',(0,0,.97),(0,0,1.18),'Hips'); bone('Chest',(0,0,1.18),(0,0,1.46),'Spine'); bone('Neck',(0,0,1.46),(0,0,1.54),'Chest'); bone('Head',(0,0,1.54),(0,0,1.74),'Neck')
    for s,side in [(1,'Left'),(-1,'Right')]:
        bone(side+'Shoulder',(s*.045,0,1.43),(s*.22,0,1.41),'Chest')
        bone(side+'UpperArm',(s*.22,0,1.41),(s*.434,0,1.15),side+'Shoulder')
        bone(side+'LowerArm',(s*.434,0,1.15),(s*.578,-.004,.943),side+'UpperArm')
        bone(side+'Hand',(s*.578,-.004,.943),(s*.665,-.021,.818),side+'LowerArm')
        bone(side+'UpperLeg',(s*.1,0,.83),(s*.115,0,.49),'Hips')
        bone(side+'LowerLeg',(s*.115,0,.49),(s*.126,.005,.135),side+'UpperLeg')
        bone(side+'Foot',(s*.126,.005,.135),(s*.126,-.18,.05),side+'LowerLeg')
        bone(side+'Toes',(s*.126,-.12,.047),(s*.126,-.2,.047),side+'Foot')
    bpy.ops.object.mode_set(mode='OBJECT'); rig.show_in_front=True; rig.data.display_type='OCTAHEDRAL'
    rig['unity_humanoid_mapping']='Hips, Spine, Chest, Neck, Head; Left/Right Shoulder, UpperArm, LowerArm, Hand, UpperLeg, LowerLeg, Foot, Toes'
    return rig

def export_worker():
    os.makedirs(SOURCE,exist_ok=True); os.makedirs(OUTPUT+'/Model',exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for name in ('SovietWorker_Body','SovietWorker_Rig'): bpy.data.objects[name].select_set(True)
    bpy.context.view_layer.objects.active=bpy.data.objects['SovietWorker_Rig']
    bpy.ops.wm.save_as_mainfile(filepath=SOURCE+'/SovietWorker.blend')
    bpy.ops.export_scene.fbx(filepath=OUTPUT+'/Model/SovietWorker.fbx',use_selection=True,object_types={'MESH','ARMATURE'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE',mesh_smooth_type='FACE')
    return {'blend':SOURCE+'/SovietWorker.blend','fbx':OUTPUT+'/Model/SovietWorker.fbx'}

def render_preview(view='front',wire=False):
    """Create a studio preview separately; no effect on exported body or its textures."""
    collection=bpy.data.collections[COLLECTION]
    for obj in list(bpy.data.objects):
        if obj.name.startswith('WorkerPreview_'): bpy.data.objects.remove(obj,do_unlink=True)
    scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE_NEXT'
    scene.render.resolution_x=900; scene.render.resolution_y=1100; scene.render.resolution_percentage=100
    world=bpy.data.worlds.new('WorkerPreview_World') if bpy.data.worlds.get('WorkerPreview_World') is None else bpy.data.worlds['WorkerPreview_World']; scene.world=world; world.use_nodes=True; world.node_tree.nodes['Background'].inputs[0].default_value=(.1,.11,.12,1); world.node_tree.nodes['Background'].inputs[1].default_value=.4
    coords={'front':(0,-4.5,1.12),'threequarter':(2.7,-4,1.7),'back':(0,4.5,1.3),'face':(.43,-1.2,1.67)}
    loc=coords.get(view,coords['front']); target=Vector((0,0,1.61 if view=='face' else .91))
    camdata=bpy.data.cameras.new('WorkerPreview_Camera'); cam=bpy.data.objects.new('WorkerPreview_Camera',camdata); scene.collection.objects.link(cam); cam.location=loc; cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler(); camdata.type='ORTHO'; camdata.ortho_scale=.40 if view=='face' else 2.03; scene.camera=cam
    for i,(loc,power,size) in enumerate([((-2,-3,4),330,3),((2,-1,2.8),150,2),((1,2,3),220,2)]):
        lightdata=bpy.data.lights.new('WorkerPreview_Light'+str(i),'AREA'); lightdata.energy=power; lightdata.shape='DISK'; lightdata.size=size
        light=bpy.data.objects.new('WorkerPreview_Light'+str(i),lightdata); scene.collection.objects.link(light); light.location=loc; light.rotation_euler=(Vector((0,0,1))-light.location).to_track_quat('-Z','Y').to_euler()
    if wire:
        body=bpy.data.objects['SovietWorker_Body']; lines=body.copy(); lines.data=body.data.copy(); lines.name='WorkerPreview_Wire'; scene.collection.objects.link(lines)
        ink=bpy.data.materials.get('WorkerPreview_WireInk') or bpy.data.materials.new('WorkerPreview_WireInk'); ink.diffuse_color=(.008,.012,.016,1); lines.data.materials.clear(); lines.data.materials.append(ink)
        mod=lines.modifiers.new('Topology edges','WIREFRAME'); mod.thickness=.0009; mod.offset=1; mod.use_replace=True; mod.use_even_offset=True
    scene.view_settings.view_transform='Standard'; scene.render.image_settings.file_format='PNG'
    os.makedirs(SOURCE+'/Previews',exist_ok=True); scene.render.filepath=SOURCE+'/Previews/'+view+('_wire' if wire else '')+'.png'; bpy.ops.render.render(write_still=True)
    return scene.render.filepath
