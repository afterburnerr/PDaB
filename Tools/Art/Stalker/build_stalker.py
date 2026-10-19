"""Original skinned entrance stalker. Execute inside Blender 4.5 through MCP.
Front is -Y, floor is Z=0. Only Stalker collection and Stalker actions are replaced.
The mottled diffuse atlas is authored algorithmically, no third party art/assets.
"""
import bpy, bmesh, math, os, json, random
from mathutils import Vector, Euler
ROOT='/home/afterburner/Projects/unity/FPLab'
ART=ROOT+'/Tools/Art/Stalker'
OUT=ROOT+'/Assets/Characters/Stalker'
REG={'face':(0,.5,.5,1),'skin':(.5,1,.5,1),'bone':(0,.5,0,.5),'cloth':(.5,.75,0,.5),'dark':(.75,.875,0,.5),'eye':(.875,1,0,.5)}
def face_uv(p):
    x,y,z=p; marks=[(1.56,0),(1.674,.25),(1.748,.375),(1.81,.645),(1.86,.78),(1.98,1)]
    v=0 if z<1.56 else 1
    for (za,va),(zb,vb) in zip(marks,marks[1:]):
        if za<=z<=zb:v=va+(vb-va)*(z-za)/(zb-za);break
    return (max(.006,min(.994,(x+.145)/.29)),v)

class Mesh:
    def __init__(self): self.v=[];self.f=[];self.uv=[];self.w=[]
    def vert(self,p,w): self.v.append(tuple(p));self.w.append(w);return len(self.v)-1
    def face(self,ids,region,uv=None):
        self.f.append(ids)
        if region=='face':uv=[face_uv(self.v[i]) for i in ids]
        u0,u1,v0,v1=REG[region];pad=3/1024
        uv=uv or [(0,0),(1,0),(1,1),(0,1)][:len(ids)]
        self.uv.append([(u0+pad+u*(u1-u0-2*pad),v0+pad+v*(v1-v0-2*pad)) for u,v in uv])
    def bridge(self,a,b,reg,va=0,vb=1):
        for i in range(len(a)):
            j=(i+1)%len(a);self.face([a[i],a[j],b[j],b[i]],reg,[(i/len(a),va),((i+1)/len(a),va),((i+1)/len(a),vb),(i/len(a),vb)])
    def rings(self,sections,n,reg):
        prev=None
        for k,(c,rx,ry,w) in enumerate(sections):
            ring=[self.vert((c[0]+math.sin(i*math.tau/n)*rx,c[1]+math.cos(i*math.tau/n)*ry,c[2]),w) for i in range(n)]
            if prev:self.bridge(prev,ring,reg,k/len(sections),(k+1)/len(sections))
            else:self.face(list(reversed(ring)),reg,[(.5,.5)]*n)
            prev=ring
        self.face(prev,reg,[(.5,.5)]*n)
    def tube(self,points,radii,reg,w,n=8):
        prev=None
        for k,(p,r) in enumerate(zip(points,radii)):
            direction=Vector(points[min(k+1,len(points)-1)])-Vector(points[max(k-1,0)])
            direction.normalize();ref=Vector((0,1,0)) if abs(direction.y)<.95 else Vector((1,0,0))
            a=direction.cross(ref).normalized();b=direction.cross(a).normalized()
            rx,ry=(r,r) if isinstance(r,(float,int)) else r
            weights=w[k] if isinstance(w,list) else w
            ring=[self.vert(Vector(p)+a*math.cos(i*math.tau/n)*rx+b*math.sin(i*math.tau/n)*ry,weights) for i in range(n)]
            if prev:self.bridge(prev,ring,reg,k/len(points),(k+1)/len(points))
            else:self.face(list(reversed(ring)),reg,[(.5,.5)]*n)
            prev=ring
        self.face(prev,reg,[(.5,.5)]*n)
    def ellipsoid(self,c,s,reg,w,n=16,rings=9,sculpt=None):
        prev=None
        for j in range(rings+1):
            lat=-math.pi/2+.002+(math.pi-.004)*j/rings
            ring=[]
            for i in range(n):
                a=i*math.tau/n
                p=Vector((c[0]+s[0]*math.cos(lat)*math.sin(a),c[1]+s[1]*math.cos(lat)*math.cos(a),c[2]+s[2]*math.sin(lat)))
                if sculpt:p=sculpt(p)
                ring.append(self.vert(p,w))
            if prev:
                if reg=='face':
                    for i in range(n):
                        ni=(i+1)%n;ids=[prev[i],prev[ni],ring[ni],ring[i]]
                        self.face(ids,'face' if sum(self.v[q][1] for q in ids)/4<-.195 else 'skin')
                else:self.bridge(prev,ring,reg,j/rings,(j+1)/rings)
            prev=ring
    def finish(self,col):
        data=bpy.data.meshes.new('Stalker_HandBuiltTopology');data.from_pydata(self.v,[],self.f);data.update()
        ob=bpy.data.objects.new('Stalker_Body',data);col.objects.link(ob)
        uv=data.uv_layers.new(name='StalkerDiffuseUV')
        for face,coords in zip(data.polygons,self.uv):
            face.use_smooth=True
            for loop,coord in zip(face.loop_indices,coords):uv.data[loop].uv=coord
        for ws in self.w:
            for name in ws:
                if not ob.vertex_groups.get(name):ob.vertex_groups.new(name=name)
        for i,ws in enumerate(self.w):
            total=sum(ws.values())
            for name,value in ws.items():
                if value>0:ob.vertex_groups[name].add([i],value/total,'REPLACE')
        bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(data);bm.free();data.update()
        return ob

def texture():
    os.makedirs(OUT+'/Textures',exist_ok=True)
    img=bpy.data.images.get('StalkerDiffuse')
    if img:bpy.data.images.remove(img)
    img=bpy.data.images.new('StalkerDiffuse',width=1024,height=1024,alpha=False)
    pixels=[0.0]*(1024*1024*4)
    rng=random.Random(43891)
    palettes={'skin':(.25,.225,.19),'bone':(.285,.25,.17),'cloth':(.037,.039,.031),'dark':(.0007,.0006,.0005),'eye':(.042,.049,.043)}
    for reg,(u0,u1,v0,v1) in REG.items():
        x0,y0=int(u0*1024),int(v0*1024);w,h=int((u1-u0)*1024),int((v1-v0)*1024)
        if reg=='face':
            src=bpy.data.images.load(OUT+'/Textures/StalkerFaceSource.png',check_existing=False);src.scale(w,h);data=list(src.pixels)
            for y in range(h):
                for x in range(w):
                    a=((y0+y)*1024+x0+x)*4;b=(y*w+x)*4;pixels[a:a+4]=[v*.43 for v in data[b:b+3]]+[1]
            bpy.data.images.remove(src);continue
        for y in range(h):
            for x in range(w):
                grain=(rng.random()-.5)*.055
                cloud=(math.sin(x*.047+math.sin(y*.032)*2)+math.cos(y*.061+math.sin(x*.02)*3)+math.sin((x+y)*.013))*.052
                vein=max(0,.12-abs(math.sin(x*.017+math.sin(y*.032)*.6)))*.35
                dirt=max(0,math.sin(x*.021)*math.cos(y*.019)-.25)*.14
                if reg=='cloth':grain*=.30;cloud*=.5;grain+=.008*math.sin(x*math.pi)+.008*math.sin(y*math.pi)
                if reg in ('dark','eye'):grain*=.001;cloud*=.001;dirt=0;vein=0
                rgb=[max(.002,min(1,c+grain+cloud-dirt-vein)) for c in palettes[reg]]
                if reg=='skin':rgb[0]-=vein*.45;rgb[1]-=dirt*.15
                i=((y0+y)*1024+x0+x)*4;pixels[i:i+4]=rgb+[1]
    img.pixels.foreach_set(pixels);img.filepath_raw=OUT+'/Textures/StalkerDiffuse.png';img.file_format='PNG';img.save();img.pack()
    mat=bpy.data.materials.get('Stalker_Atlas') or bpy.data.materials.new('Stalker_Atlas');mat.use_nodes=True
    ns=mat.node_tree.nodes;ns.clear();out=ns.new('ShaderNodeOutputMaterial');bs=ns.new('ShaderNodeBsdfPrincipled');tx=ns.new('ShaderNodeTexImage');tx.image=img
    bs.inputs['Roughness'].default_value=.94;bs.inputs['Specular IOR Level'].default_value=.12
    mat.node_tree.links.new(tx.outputs['Color'],bs.inputs['Base Color']);mat.node_tree.links.new(bs.outputs['BSDF'],out.inputs['Surface'])
    return mat

def build():
    old=bpy.data.collections.get('Stalker')
    if old:
        for ob in list(old.all_objects):bpy.data.objects.remove(ob,do_unlink=True)
        bpy.data.collections.remove(old)
    for a in list(bpy.data.actions):
        if a.name.startswith('Stalker_'):bpy.data.actions.remove(a)
    col=bpy.data.collections.new('Stalker');bpy.context.scene.collection.children.link(col)
    m=Mesh();W=lambda name:{name:1}
    # Gaunt continuous ribcage: pinched waist, flared clavicles, forward-curling neck.
    sections=[((0,.07,.83),.135,.09,W('Hips')),((0,.07,.94),.13,.088,{'Hips':.5,'Spine':.5}),((0,.055,1.02),.095,.067,W('Spine')),((0,.035,1.12),.108,.075,W('Spine')),((0,.01,1.22),.147,.097,{'Spine':.5,'Chest':.5}),((0,-.005,1.32),.183,.112,W('Chest')),((0,-.025,1.40),.219,.114,W('Chest')),((0,-.057,1.47),.205,.103,W('Chest')),((0,-.09,1.51),.11,.075,{'Chest':.4,'Neck':.6}),((0,-.17,1.57),.061,.059,W('Neck')),((0,-.24,1.65),.066,.061,W('Neck'))]
    m.rings(sections,24,'skin')
    # Raised bone landmarks under skin, asymmetric ribs and collarbones.
    for s,side in [(1,'Left'),(-1,'Right')]:
        for k in range(5):
            z=1.17+k*.046;rx=.12+k*.012
            bodyrx=.108+(z-1.12)*.43;bodyry=.075+(z-1.12)*.105;cy=.035-(z-1.12)*.23
            pts=[(s*rx*t,cy-bodyry*math.sqrt(max(.05,1-(rx*t/bodyrx)**2))+.002,z-.012*math.sin(t*math.pi)) for t in (.12,.35,.65,.9,1)]
            m.tube(pts,[.002,.004,.005,.004,.001],'skin',W('Chest'),6)
        m.tube([(s*.015,-.145,1.455),(s*.075,-.142,1.46),(s*.155,-.12,1.447),(s*.215,-.075,1.448)],[.008,.012,.011,.004],'skin',W('Chest'),8)
        m.ellipsoid((s*.192,-.045,1.43),(.069,.075,.091),'skin',W(side+'UpperArm'),12,7)
        # Uneven thin arms, multiple closely spaced elbow rings support bending.
        pts=[(s*.218,-.05,1.44),(s*.252,-.049,1.365),(s*.295,-.06,1.275),(s*.335,-.074,1.185),(s*.346,-.085,1.16),(s*.356,-.102,1.135),(s*.381,-.14,1.03),(s*.404,-.18,.90),(s*.419,-.209,.793)]
        weights=[W(side+'UpperArm')]*3+[ {side+'UpperArm':.75,side+'LowerArm':.25},{side+'UpperArm':.5,side+'LowerArm':.5},{side+'UpperArm':.25,side+'LowerArm':.75}]+[W(side+'LowerArm')]*3
        m.tube(pts,[.059,.052,.042,.036,.038,.036,.042,.031,.023],'skin',weights,12)
        # Tendons on the forearms and a narrow elongated bony palm.
        m.tube([(s*.365,-.128,1.11),(s*.39,-.185,.98),(s*.418,-.23,.80)],[.004,.006,.002],'skin',W(side+'LowerArm'),6)
        m.tube([(s*.419,-.209,.80),(s*.426,-.228,.75),(s*.432,-.251,.695),(s*.438,-.264,.665)],[(.025,.018),(.041,.019),(.047,.017),(.038,.013)],'skin',W(side+'Hand'),12)
        for j in range(4):
            x=s*(.408+j*.018);length=(.135,.17,.158,.12)[j]
            pts=[(x,-.265,.686),(x+s*.006,-.28,.635),(x+s*.013,-.303,.686-length*.78),(x+s*.015,-.328,.686-length),(x+s*.01,-.345,.688-length)]
            m.tube(pts,[.011,.010,.008,.006,.0025],'skin',W(side+'Hand'),7)
            m.tube([pts[-2],(x+s*.01,-.361,.699-length)],[.006,.0005],'bone',W(side+'Hand'),5)
        m.tube([(s*.403,-.235,.755),(s*.375,-.257,.713),(s*.366,-.29,.666),(s*.38,-.325,.647)],[.016,.015,.011,.003],'skin',W(side+'Hand'),8)
        # Tattered high-waisted trousers, narrow ankles; knees remain bendable.
        legpts=[(s*.097,.065,.91),(s*.112,.066,.82),(s*.119,.06,.70),(s*.125,.034,.57),(s*.127,.023,.535),(s*.128,.022,.50),(s*.129,.037,.40),(s*.13,.048,.27),(s*.13,.049,.205)]
        legweights=[W('Hips'),{'Hips':.5,side+'UpperLeg':.5},W(side+'UpperLeg')]+[{side+'UpperLeg':.8,side+'LowerLeg':.2},{side+'UpperLeg':.5,side+'LowerLeg':.5},{side+'UpperLeg':.2,side+'LowerLeg':.8}]+[W(side+'LowerLeg')]*3
        m.tube(legpts,[(.077,.08),(.078,.079),(.065,.065),(.053,.054),(.051,.051),(.052,.051),(.046,.049),(.036,.043),(.029,.035)],'cloth',legweights,12)
        m.ellipsoid((s*.13,-.03,.068),(.047,.113,.054),'skin',W(side+'Foot'),12,7)
        m.tube([(s*.13,.049,.09),(s*.13,.049,.235)],[.03,.032],'skin',[W(side+'Foot'),W(side+'LowerLeg')],10)
        for j in range(5):
            xx=s*(.095+j*.017);yy=-.125+abs(j-1)*.007
            m.tube([(xx,-.076,.055),(xx,yy,.043),(xx,yy-.028,.034)],[.012,.011,.006],'skin',W(side+'Foot'),7)
    m.rings([((0,.065,.81),.172,.104,W('Hips')),((0,.065,.87),.18,.112,W('Hips')),((0,.065,.93),.16,.105,W('Hips')),((0,.061,.949),.13,.09,W('Hips'))],24,'cloth')
    # Back ridge, visible in silhouette and in flashlight raking light.
    for j in range(9):
        z=1.03+j*.052;y=.127-j*.005
        m.ellipsoid((0,y,z),(.014,.017,.016),'skin',W('Spine') if j<4 else W('Chest'),8,5)
    # Skull sculpted from a dense lowpoly surface, not a textured sphere.
    def skull(p):
        x,y,z=p
        if y<-.19:
            front=max(0,min(1,(-y-.19)/.105))
            # Deep paired orbital depressions, hollow temples and protruding cheekbones.
            eye=sum(math.exp(-((x-s*.062)/.040)**2-((z-1.808)/.045)**2) for s in (-1,1))
            cheek=sum(math.exp(-((x-s*.105)/.035)**2-((z-1.733)/.038)**2) for s in (-1,1))
            brow=sum(math.exp(-((x-s*.061)/.055)**2-((z-1.853)/.019)**2) for s in (-1,1))
            y+=front*(.095*eye-.014*cheek-.013*brow)
        return Vector((x,y,z))
    m.ellipsoid((0,-.195,1.792),(.137,.119,.188),'face',W('Head'),40,26,skull)
    # Actual recessed oval eyes, with albedo-mapped human sclera/iris and asymmetric lids.
    for s in (-1,1):
        cx=s*.062;cz=1.81+(0.002 if s<0 else -.002)
        m.ellipsoid((cx,-.276,cz),(.033,.018,.024),'face',W('Head'),18,11)
        m.ellipsoid((cx,-.291,cz),(.026,.014,.017),'face',W('Head'),18,11)
        # Thin eyelid rims are embedded into the cheek rather than floating rings.
        eyepts=[]
        for i in range(21):
            a=i*math.tau/20;upper=max(0,math.sin(a));droop=.0035*upper if s>0 else 0
            eyepts.append((cx+math.cos(a)*.033,-.297+abs(math.cos(a))*.013,cz+math.sin(a)*.023-droop))
        m.tube(eyepts,[.0035 if i<11 else .0025 for i in range(21)],'face',W('Head'),6)
        # Soft cheekbones and temples under skin, without rigid angular bars or black ear plates.
        m.tube([(s*.03,-.283,1.761),(s*.068,-.279,1.746),(s*.106,-.257,1.753)],[.003,.006,.003],'face',W('Head'),7)
        m.ellipsoid((s*.139,-.178,1.777),(.018,.023,.041),'skin',W('Head'),12,9)
        m.tube([(s*.15,-.195,1.799),(s*.153,-.199,1.78),(s*.146,-.194,1.757)],[.005,.004,.003],'skin',W('Head'),6)
    # Nasal bone ridge, bent tip, actual paired black nostrils.
    m.tube([(0,-.298,1.846),(0,-.329,1.803),(0,-.35,1.763),(0,-.338,1.748)],[(.016,.009),(.018,.013),(.022,.016),(.019,.009)],'face',W('Head'),8)
    # Wide vertically stretched black mouth, protruding jaw and exposed irregular teeth.
    m.ellipsoid((0,-.293,1.665),(.098,.044,.077),'dark',W('Head'),26,14)
    mouthpts=[(.096*math.cos(a),-.315+.027*abs(math.cos(a)),1.667+.078*math.sin(a)) for a in [i*math.tau/30 for i in range(31)]]
    m.tube(mouthpts,[.004 if i<16 else .007 for i in range(31)],'face',W('Head'),7)
    # Thin sunken lower jaw/chin bulges forward below mouth.
    m.tube([(-.105,-.267,1.676),(-.088,-.295,1.607),(0,-.319,1.575),(.088,-.295,1.607),(.105,-.267,1.676)],[.014,.019,.023,.019,.014],'face',W('Head'),9)
    for row in (0,1):
        gum=[]
        for i in range(17):
            t=(i-8)/8;gum.append((t*.092,-.329+.043*t*t,(1.723-.018*t*t) if row==0 else (1.611+.023*t*t)))
        m.tube(gum,[.010]*17,'face',W('Head'),8)
    rng=random.Random(374)
    for row in (0,1):
        for j in range(13):
            if (row,j) in ((0,2),(1,10)):continue
            t=(j-6)/6;x=t*.086+rng.uniform(-.0025,.0025);y=-.340+.042*t*t+rng.uniform(-.003,.001)
            z=(1.722-.018*t*t) if row==0 else (1.612+.023*t*t)
            z+=rng.uniform(-.005,.004)
            length=(.036 if row==0 else .031)*rng.uniform(.72,1.22);direction=-1 if row==0 else 1
            width=rng.uniform(.0052,.0071);lean=rng.uniform(-.005,.005)
            m.tube([(x,y,z),(x+lean*.5,y-.004,z+direction*length*.45),(x+lean,y-.002,z+direction*length)],[(width*.83,.0045),(width,.005),(width*rng.uniform(.72,.95),.0037)],'bone',W('Head'),7)
    for i,ws in enumerate(m.w):
        if ws.get('Head',0)==1:m.v[i]=(m.v[i][0],m.v[i][1]-.055,m.v[i][2])
    ob=m.finish(col);ob.data.materials.append(texture());rig=rigging(col)
    mod=ob.modifiers.new('Stalker weighted skeleton','ARMATURE');mod.object=rig;ob.parent=rig
    ob['design']='Original bald gaunt entrance pursuer; carved eye sockets, projecting cheekbones and nasal bridge, exposed irregular teeth, separated hooked fingers.'
    ob['front']='-Y';ob['texture']='Original procedural painted mottled diffuse atlas, 1024 square, no emission.'
    bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.unit_settings.scale_length=1
    animate(rig);ob.data.calc_loop_triangles()
    result={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles),'bones':len(rig.data.bones),'actions':[a.name for a in bpy.data.actions if a.name.startswith('Stalker_')]}
    print(json.dumps(result));return result

def rigging(col):
    bpy.ops.object.select_all(action='DESELECT');data=bpy.data.armatures.new('Stalker_Skeleton');rig=bpy.data.objects.new('Stalker_Rig',data);col.objects.link(rig);rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
    def bone(n,h,t,p=None):
        b=data.edit_bones.new(n);b.head=h;b.tail=t
        if p:b.parent=data.edit_bones[p]
    bone('Root',(0,0,0),(0,0,.2));bone('Hips',(0,.065,.84),(0,.06,1.0),'Root');bone('Spine',(0,.06,1.0),(0,.01,1.25),'Hips');bone('Chest',(0,.01,1.25),(0,-.08,1.49),'Spine');bone('Neck',(0,-.08,1.49),(0,-.24,1.65),'Chest');bone('Head',(0,-.24,1.65),(0,-.25,1.95),'Neck')
    for s,side in [(1,'Left'),(-1,'Right')]:
        bone(side+'Shoulder',(s*.04,-.06,1.47),(s*.218,-.05,1.44),'Chest');bone(side+'UpperArm',(s*.218,-.05,1.44),(s*.346,-.085,1.16),side+'Shoulder');bone(side+'LowerArm',(s*.346,-.085,1.16),(s*.419,-.209,.793),side+'UpperArm');bone(side+'Hand',(s*.419,-.209,.793),(s*.438,-.264,.665),side+'LowerArm')
        bone(side+'UpperLeg',(s*.097,.065,.89),(s*.127,.023,.535),'Hips');bone(side+'LowerLeg',(s*.127,.023,.535),(s*.13,.049,.15),side+'UpperLeg');bone(side+'Foot',(s*.13,.049,.15),(s*.13,-.09,.05),side+'LowerLeg')
    bpy.ops.object.mode_set(mode='OBJECT');rig.show_in_front=True;rig.data.display_type='OCTAHEDRAL';return rig

def animate(rig):
    scene=bpy.context.scene;scene.render.fps=30;rig.animation_data_create()
    lengths={'Idle':90,'Walk':36,'Run':24,'Recoil':30,'Attack':36}
    for clip,length in lengths.items():
        action=bpy.data.actions.new('Stalker_'+clip);rig.animation_data.action=action;action.use_fake_user=True
        for frame in range(1,length+1):
            t=(frame-1)/(length-1);phase=t*math.tau
            for b in rig.pose.bones:b.rotation_mode='XYZ';b.rotation_euler=(0,0,0);b.location=(0,0,0)
            def rot(n,x=0,y=0,z=0):
                rest=rig.data.bones[n].matrix_local.to_quaternion()
                rig.pose.bones[n].rotation_euler=(rest.inverted() @ Euler((x,y,z),'XYZ').to_quaternion() @ rest).to_euler('XYZ')
            if clip=='Idle':
                rot('Chest',.015*math.sin(phase));rot('Head',-.025,.035*math.sin(phase),.04*math.sin(phase));rot('Neck',0,0,.02*math.sin(phase*2));rot('LeftLowerArm',.05+.02*math.sin(phase));rot('RightLowerArm',.06-.025*math.sin(phase))
            elif clip in ('Walk','Run'):
                run=clip=='Run';amp=.78 if run else .46
                rig.pose.bones['Hips'].location=rig.data.bones['Hips'].matrix_local.to_quaternion().inverted() @ Vector((0,0,(.022 if run else .012)*(1-math.cos(phase*2))))
                rot('Spine',.14 if run else .035);rot('Chest',.03,0,.035*math.sin(phase));rot('Head',-.04,0,-.04*math.sin(phase))
                for sign,side in [(1,'Left'),(-1,'Right')]:
                    s=math.sin(phase)*sign;rot(side+'UpperLeg',amp*s);rot(side+'LowerLeg',.70*max(0,-s) if run else .38*max(0,-s));rot(side+'Foot',.14*s)
                    rot(side+'UpperArm',-amp*.7*s-.12 if run else -amp*.65*s);rot(side+'LowerArm',-.18-.3*max(0,s));rot(side+'Hand',.05,0,.04*sign)
            elif clip=='Recoil':
                e=math.sin(math.pi*t)**2;rot('Spine',-.27*e);rot('Chest',-.21*e);rot('Head',-.25*e,0,.16*e);rot('LeftUpperArm',-.25*e,0,-.34*e);rot('RightUpperArm',-.22*e,0,.34*e);rot('LeftLowerArm',-.8*e);rot('RightLowerArm',-.8*e)
            else:
                reach=math.sin(math.pi*t)**.6;strike=math.sin(math.pi*min(1,max(0,(t-.2)/.65)))
                rot('Spine',.22*strike);rot('Chest',.20*strike);rot('Head',-.14*reach,0,.10*math.sin(phase));rig.pose.bones['Hips'].location.y=-.035*strike
                for sign,side in [(1,'Left'),(-1,'Right')]:rot(side+'UpperArm',-1.28*reach,0,sign*.06*reach);rot(side+'LowerArm',-.30*reach);rot(side+'Hand',.35*reach)
            for b in rig.pose.bones:
                b.keyframe_insert(data_path='rotation_euler',frame=frame,group=b.name);b.keyframe_insert(data_path='location',frame=frame,group=b.name)
        # NLA strips make all clips discoverable by FBX all-actions export.
        track=rig.animation_data.nla_tracks.new();track.name=clip;strip=track.strips.new(clip,1,action);strip.name=clip;track.mute=True
    rig.animation_data.action=None
    for b in rig.pose.bones:b.rotation_euler=(0,0,0);b.location=(0,0,0)
    scene.frame_set(1)

def export():
    os.makedirs(ART,exist_ok=True);os.makedirs(OUT+'/Model',exist_ok=True)
    rig=bpy.data.objects['Stalker_Rig'];rig.animation_data.action=None
    for track in rig.animation_data.nla_tracks:track.mute=True
    for b in rig.pose.bones:b.rotation_euler=(0,0,0);b.location=(0,0,0)
    bpy.ops.object.select_all(action='DESELECT')
    for name in ('Stalker_Body','Stalker_Rig'):bpy.data.objects[name].select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.wm.save_as_mainfile(filepath=ART+'/Stalker.blend')
    bpy.ops.export_scene.fbx(filepath=OUT+'/Model/Stalker.fbx',use_selection=True,object_types={'MESH','ARMATURE'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,bake_anim_step=1,path_mode='RELATIVE',mesh_smooth_type='FACE')
    return {'fbx':OUT+'/Model/Stalker.fbx','blend':ART+'/Stalker.blend'}

def preview(view='threequarter',clip=None,frame=1):
    col=bpy.data.collections['Stalker']
    # Hide other authoring collections in render without deleting their contents.
    for collection in bpy.data.collections:
        if collection!=col:collection.hide_render=True
    for ob in list(bpy.data.objects):
        if ob.name.startswith('StalkerPreview_'):bpy.data.objects.remove(ob,do_unlink=True)
    rig=bpy.data.objects['Stalker_Rig'];rig.animation_data.action=bpy.data.actions.get('Stalker_'+clip) if clip else None
    for track in rig.animation_data.nla_tracks:track.mute=True
    if not clip:
        for b in rig.pose.bones:b.rotation_euler=(0,0,0);b.location=(0,0,0)
    scene=bpy.context.scene;scene.frame_set(frame);scene.render.engine='BLENDER_EEVEE_NEXT';scene.render.resolution_x=900;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    world=bpy.data.worlds.new('StalkerPreview_World') if not bpy.data.worlds.get('StalkerPreview_World') else bpy.data.worlds['StalkerPreview_World'];scene.world=world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.025,.031,.027,1);world.node_tree.nodes['Background'].inputs[1].default_value=.3
    loc=(2.9,-4.4,1.8) if view=='threequarter' else ((.24,-1.25,1.83) if view=='face' else (0,-4,1.1));target=Vector((0,-.1,.99)) if view!='face' else Vector((0,-.26,1.78))
    cd=bpy.data.cameras.new('StalkerPreview_Camera');cam=bpy.data.objects.new('StalkerPreview_Camera',cd);scene.collection.objects.link(cam);cam.location=loc;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cd.type='ORTHO';cd.ortho_scale=.49 if view=='face' else 2.14;scene.camera=cam
    for i,(loc,power,size) in enumerate([((-1.4,-3,3.2),220,2),((2,-1,2.5),65,1.5),((.5,2,3),170,2)]):
        d=bpy.data.lights.new('StalkerPreview_Light'+str(i),'AREA');d.energy=power;d.shape='DISK';d.size=size;o=bpy.data.objects.new('StalkerPreview_Light'+str(i),d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler()
    scene.view_settings.view_transform='Standard';scene.render.image_settings.file_format='PNG';os.makedirs(ART+'/Previews',exist_ok=True);scene.render.filepath=ART+'/Previews/'+view+('_'+clip if clip else '')+'.png';bpy.ops.render.render(write_still=True);return scene.render.filepath
