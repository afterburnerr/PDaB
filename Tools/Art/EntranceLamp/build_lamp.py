"""Original Soviet round opal ceiling fixture, authored through live Blender MCP.
Only EntranceLamp collection is replaced; unrelated authoring objects are preserved.
Local origin = ceiling centre. Blender -Z is down; FBX converts to Unity -Y.
"""
import bpy, bmesh, math, os, json
from mathutils import Vector
ROOT='/home/afterburner/Projects/unity/FPLab'
SOURCE=ROOT+'/Tools/Art/EntranceLamp'
OUT=ROOT+'/Assets/Art/Environment/EntranceLamp'
COLLECTION='EntranceLamp'

def image_material(kind,base,roughness,metallic=0):
    size=512
    name='EntranceLamp_'+kind
    texpath=OUT+'/Textures/Lamp'+kind+'_Diffuse.png'
    os.makedirs(os.path.dirname(texpath),exist_ok=True)
    old=bpy.data.images.get(name+'_Diffuse')
    if old: bpy.data.images.remove(old)
    im=bpy.data.images.new(name+'_Diffuse',width=size,height=size,alpha=False)
    pixels=[]
    for y in range(size):
        for x in range(size):
            # Original deterministic procedural diffuse. No light/shadow baked in.
            n=(((x*73856093)^(y*19349663))%1009)/1009
            cloud=(math.sin(x*.041+math.sin(y*.023)*3)+math.sin(y*.031+x*.011)+math.sin(x*.013-y*.019))/3
            fleck=0
            if kind=='Metal':
                grain=.025*(n-.5)+cloud*.028
                # Small scattered paint loss, a few oxidized freckles, not a camouflage texture.
                chipped=n>.967 and (math.sin(x*.061)*math.sin(y*.047)+cloud)>.47
                col=(.31+grain,.285+grain,.226+grain) if chipped else tuple(c+grain for c in base)
            elif kind=='Glass':
                grain=.018*(n-.5)+cloud*.011
                col=tuple(c+grain for c in base)
            else:
                grain=.025*(n-.5)+cloud*.016
                col=tuple(c+grain for c in base)
            pixels.extend([max(0,min(1,c)) for c in col]+[1])
    im.pixels.foreach_set(pixels); im.filepath_raw=texpath; im.file_format='PNG'; im.save(); im.pack()
    mat=bpy.data.materials.get(name) or bpy.data.materials.new(name); mat.use_nodes=True
    nodes=mat.node_tree.nodes; nodes.clear(); out=nodes.new('ShaderNodeOutputMaterial'); bsdf=nodes.new('ShaderNodeBsdfPrincipled'); tex=nodes.new('ShaderNodeTexImage'); tex.image=im
    bsdf.inputs['Roughness'].default_value=roughness; bsdf.inputs['Metallic'].default_value=metallic
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color']); mat.node_tree.links.new(bsdf.outputs['BSDF'],out.inputs['Surface'])
    if kind=='Glass':
        # Opal glass scatters light; it is not a clear open lampshade.
        bsdf.inputs['Subsurface Weight'].default_value=.08
        bsdf.inputs['Emission Color'].default_value=(.8,.73,.54,1); bsdf.inputs['Emission Strength'].default_value=.08
    return mat

def mesh_obj(name,verts,faces,collection,mat,uvmode='lathe'):
    mesh=bpy.data.meshes.new(name+'_Mesh'); mesh.from_pydata(verts,[],faces); mesh.update()
    bm=bmesh.new(); bm.from_mesh(mesh); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(mesh); bm.free()
    obj=bpy.data.objects.new(name,mesh); collection.objects.link(obj); mesh.materials.append(mat)
    uv=mesh.uv_layers.new(name='UVMap')
    for p in mesh.polygons:
        p.use_smooth=True
        coords=[mesh.vertices[mesh.loops[li].vertex_index].co for li in p.loop_indices]
        angles=[math.atan2(c.y,c.x)/math.tau+.5 for c in coords]
        if max(angles)-min(angles)>.5: angles=[u+1 if u<.5 else u for u in angles]
        for i,li in enumerate(p.loop_indices):
            c=coords[i]
            uv.data[li].uv=((c.x/.36+.5,c.y/.36+.5) if uvmode=='planar' else (angles[i],-c.z/.19))
    return obj

def lathe(name,profile,segments,collection,mat,ribs=0):
    verts=[]; rings=[]; faces=[]
    for radius,z in profile:
        if radius==0:
            rings.append([len(verts)]); verts.append((0,0,z)); continue
        ring=[]
        for i in range(segments):
            a=i*math.tau/segments
            # Small convex radial ribs pressed into the opal lens, tapering at centre/rim.
            strength=math.sin(min(1,radius/.172)*math.pi)*.0032 if ribs else 0
            rr=radius+strength*(.5+.5*math.cos(a*ribs))
            ring.append(len(verts)); verts.append((rr*math.cos(a),rr*math.sin(a),z))
        rings.append(ring)
    for k in range(len(rings)-1):
        a,b=rings[k],rings[k+1]
        for i in range(segments):
            j=(i+1)%segments
            if len(a)==1 and len(b)>1: faces.append((a[0],b[i],b[j]))
            elif len(b)==1 and len(a)>1: faces.append((a[i],b[0],a[j]))
            elif len(a)>1 and len(b)>1: faces.append((a[i],b[i],b[j],a[j]))
    return mesh_obj(name,verts,faces,collection,mat,'planar' if ribs else 'lathe')

def clip(name,angle,collection,mat):
    # A folded retaining tongue: follows the rim and hooks over the glass edge.
    profile=[(.192,-.021),(.2,-.021),(.207,-.061),(.203,-.083),(.173,-.087),(.173,-.077),(.195,-.073),(.198,-.061)]
    verts=[]
    for tangent in (-.009,.009):
        for radius,z in profile:
            verts.append((radius*math.cos(angle)-tangent*math.sin(angle),radius*math.sin(angle)+tangent*math.cos(angle),z))
    n=len(profile); faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    for i in range(n): j=(i+1)%n; faces.append((i,j,n+j,n+i))
    obj=mesh_obj(name,verts,faces,collection,mat)
    bevel=obj.modifiers.new('Rounded folded steel edges','BEVEL'); bevel.width=.0008; bevel.segments=1
    bpy.context.view_layer.objects.active=obj; obj.select_set(True); bpy.ops.object.modifier_apply(modifier=bevel.name); obj.select_set(False)
    return obj

def screw(name,angle,collection,mat):
    r=.191; x=r*math.cos(angle); y=r*math.sin(angle)
    bpy.ops.mesh.primitive_cylinder_add(vertices=16,radius=.0048,depth=.0026,location=(x,y,-.0877))
    obj=bpy.context.object; obj.name=name
    for c in list(obj.users_collection): c.objects.unlink(obj)
    collection.objects.link(obj); obj.data.materials.append(mat)
    # Real recessed slot, not a dark painted cross or a raised strip.
    bpy.ops.mesh.primitive_cube_add(size=1,location=(x,y,-.089))
    cutter=bpy.context.object; cutter.name='LampSlotTemporary'; cutter.dimensions=(.012,.0014,.0016); cutter.rotation_euler.z=angle+.35
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    bpy.context.view_layer.objects.active=obj
    boolean=obj.modifiers.new('Machined screw slot','BOOLEAN'); boolean.operation='DIFFERENCE'; boolean.solver='EXACT'; boolean.object=cutter
    bpy.ops.object.modifier_apply(modifier=boolean.name); bpy.data.objects.remove(cutter,do_unlink=True)
    bevel=obj.modifiers.new('Screw edge break','BEVEL'); bevel.width=.00022; bevel.segments=1
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    bpy.ops.object.select_all(action='DESELECT')
    return obj

def join_named(objects,name):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    if len(objects)>1: bpy.ops.object.join()
    obj=bpy.context.object; obj.name=name
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    # Every exported mesh uses the same physical ceiling origin, not each screw's origin.
    old_cursor=bpy.context.scene.cursor.location.copy(); bpy.context.scene.cursor.location=(0,0,0); bpy.ops.object.origin_set(type='ORIGIN_CURSOR'); bpy.context.scene.cursor.location=old_cursor
    for p in obj.data.polygons: p.use_smooth=True
    bevelnorm=obj.modifiers.new('Area weighted corner normals','WEIGHTED_NORMAL'); bevelnorm.keep_sharp=True; bevelnorm.weight=50
    return obj

def build_lamp():
    previous=bpy.data.collections.get(COLLECTION)
    if previous:
        for obj in list(previous.all_objects): bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(previous)
    collection=bpy.data.collections.new(COLLECTION); bpy.context.scene.collection.children.link(collection)
    bpy.ops.object.select_all(action='DESELECT')
    metal=image_material('Metal',(.14,.15,.132),.79,.45)
    glass=image_material('Glass',(.73,.71,.61),.43,0)
    fasteners=image_material('Fasteners',(.105,.103,.091),.57,.65)
    pan=lathe('LampMetal',[(0,0),(.162,0),(.185,-.006),(.198,-.018),(.202,-.032),(.202,-.052),(.195,-.063),(.178,-.063),(.175,-.045),(.173,-.025),(0,-.022)],80,collection,metal)
    lens=lathe('LampGlass',[(.172,-.054),(.172,-.069),(.166,-.096),(.153,-.122),(.133,-.146),(.106,-.163),(.074,-.175),(.039,-.181),(0,-.183)],128,collection,glass,32)
    gasket=lathe('Rim gasket',[(.176,-.053),(.18,-.054),(.18,-.062),(.176,-.066),(.176,-.053)],64,collection,fasteners)
    details=[gasket]
    for i in range(3):
        angle=math.radians(30+i*120); details.extend([clip('Retaining clip '+str(i+1),angle,collection,fasteners),screw('Slotted screw '+str(i+1),angle,collection,fasteners)])
    pan=join_named([pan],'LampMetal'); lens=join_named([lens],'LampGlass'); details=join_named(details,'LampFasteners')
    # UVs on the small boolean screws use an ordinary box projection where missing.
    for obj in (pan,lens,details):
        if not obj.data.uv_layers:
            uv=obj.data.uv_layers.new(name='UVMap')
            for poly in obj.data.polygons:
                for li in poly.loop_indices:
                    co=obj.data.vertices[obj.data.loops[li].vertex_index].co; uv.data[li].uv=(co.x/.42+.5,co.y/.42+.5)
        obj['authoring']='Original procedural Soviet residential opal-glass ceiling light'; obj['ceiling_origin']=True
    bpy.context.scene.unit_settings.system='METRIC'; bpy.context.scene.unit_settings.scale_length=1
    stats={}
    for obj in (pan,lens,details):
        obj.data.calc_loop_triangles(); stats[obj.name]={'vertices':len(obj.data.vertices),'triangles':len(obj.data.loop_triangles),'materials':[m.name for m in obj.data.materials]}
    stats['total_triangles']=sum(v['triangles'] for v in stats.values())
    os.makedirs(SOURCE,exist_ok=True)
    with open(SOURCE+'/ModelManifest.json','w') as stream: json.dump({'units':'metres','diameter':.414,'depth':.183,'origin':'ceiling centre','meshes':stats,'textures':'Original deterministic diffuse, no baked illumination'},stream,indent=2)
    print(json.dumps(stats)); return stats

def export_lamp():
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.data.collections[COLLECTION].all_objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=bpy.data.objects['LampMetal']
    os.makedirs(OUT+'/Model',exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=SOURCE+'/EntranceLamp.blend')
    bpy.ops.export_scene.fbx(filepath=OUT+'/Model/EntranceLamp.fbx',use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_mesh_modifiers=True,bake_anim=False,path_mode='RELATIVE',mesh_smooth_type='FACE')
    return OUT+'/Model/EntranceLamp.fbx'

def render_lamp():
    scene=bpy.context.scene
    # Preserve unrelated objects and their visibility, including paused worker authoring.
    states={obj.name:obj.hide_render for obj in bpy.data.objects}
    for obj in bpy.data.objects: obj.hide_render=obj.name not in ('LampMetal','LampGlass','LampFasteners')
    preview=[]
    scene.render.engine='BLENDER_EEVEE_NEXT'; scene.render.resolution_x=1100; scene.render.resolution_y=1000; scene.render.resolution_percentage=100
    world=bpy.data.worlds.new('EntranceLampPreviewWorld'); world.use_nodes=True; world.node_tree.nodes['Background'].inputs[0].default_value=(.07,.08,.09,1); world.node_tree.nodes['Background'].inputs[1].default_value=.35; oldworld=scene.world; scene.world=world
    cd=bpy.data.cameras.new('LampPreviewCamera'); cam=bpy.data.objects.new('LampPreviewCamera',cd); scene.collection.objects.link(cam); preview.append(cam); cam.location=(.42,-.59,-.43); target=Vector((0,0,-.075)); cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler(); cd.type='ORTHO'; cd.ortho_scale=.55; oldcamera=scene.camera; scene.camera=cam
    for i,(loc,energy,size) in enumerate([((.25,-.5,-.6),60,.5),((-.4,.1,-.3),32,.4),((.2,.4,-.2),22,.3)]):
        ld=bpy.data.lights.new('LampPreviewLight'+str(i),'AREA'); ld.energy=energy; ld.size=size; ob=bpy.data.objects.new('LampPreviewLight'+str(i),ld); scene.collection.objects.link(ob); preview.append(ob); ob.location=loc; ob.rotation_euler=(target-ob.location).to_track_quat('-Z','Y').to_euler()
    scene.view_settings.view_transform='AgX'; scene.render.image_settings.file_format='PNG'; os.makedirs(SOURCE+'/Previews',exist_ok=True); scene.render.filepath=SOURCE+'/Previews/EntranceLamp_Underside.png'
    try: bpy.ops.render.render(write_still=True)
    finally:
        scene.camera=oldcamera; scene.world=oldworld
        for ob in preview: bpy.data.objects.remove(ob,do_unlink=True)
        for name,state in states.items():
            if name in bpy.data.objects: bpy.data.objects[name].hide_render=state
    return scene.render.filepath

if __name__=='__main__':
    build_lamp()
    export_lamp()
