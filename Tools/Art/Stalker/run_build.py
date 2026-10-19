path='/home/afterburner/Projects/unity/FPLab/Tools/Art/Stalker/build_stalker.py'
exec(compile(open(path).read(),path,'exec'),globals())
print(build())
print(export())
print(preview('face'))
print(preview('threequarter'))
print(preview('threequarter','Attack',18))
print(preview('threequarter','Run',7))
rig=bpy.data.objects['Stalker_Rig'];ob=bpy.data.objects['Stalker_Body']
rig.animation_data.action=None
for b in rig.pose.bones:b.rotation_euler=(0,0,0);b.location=(0,0,0)
bpy.context.scene.frame_set(1);bpy.context.view_layer.update()
weights=[sum(g.weight for g in v.groups) for v in ob.data.vertices]
coords=[tuple(v.co) for v in ob.data.vertices]
lo=[min(p[k] for p in coords) for k in range(3)];hi=[max(p[k] for p in coords) for k in range(3)]
clips=[]
for name in ('Idle','Walk','Run','Recoil','Attack'):
    a=bpy.data.actions['Stalker_'+name];rig.animation_data.action=a
    bounds=[]
    for f in range(int(a.frame_range[0]),int(a.frame_range[1])+1):
        bpy.context.scene.frame_set(f);dg=bpy.context.evaluated_depsgraph_get();eo=ob.evaluated_get(dg);me=eo.to_mesh()
        p=[v.co for v in me.vertices]
        bounds.append(([min(v[k] for v in p) for k in range(3)],[max(v[k] for v in p) for k in range(3)]))
        eo.to_mesh_clear()
    clips.append({'name':name,'action':a.name,'frames':list(a.frame_range),'duration_seconds':(a.frame_range[1]-a.frame_range[0])/30,'min': [min(b[0][k] for b in bounds) for k in range(3)],'max':[max(b[1][k] for b in bounds) for k in range(3)]})
rig.animation_data.action=None
for b in rig.pose.bones:b.rotation_euler=(0,0,0);b.location=(0,0,0)
bpy.context.scene.frame_set(1)
report={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles),'bones':len(rig.data.bones),'rest_bounds_blender_min':lo,'rest_bounds_blender_max':hi,'weight_min':min(weights),'weight_max':max(weights),'unweighted':sum(x<.999 for x in weights),'max_influences':max(len(v.groups) for v in ob.data.vertices),'front_blender':'-Y','front_unity':'+Z','unit_scale':1,'clips':clips,'texture':'Assets/Characters/Stalker/Textures/StalkerDiffuse.png','texture_authorship':'Original imagegen painted face integrated with procedural mottled skin, ivory teeth and dark cloth; no emissive channels.'}
open(ART+'/Validation.json','w').write(json.dumps(report,indent=2))
print(json.dumps(report))
