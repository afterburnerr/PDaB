import bpy
import json

# This is the isolated factory scene opened for this task by Blender MCP.
for name in ("Cube", "Camera", "Light"):
    obj = bpy.data.objects.get(name)
    if obj is not None:
        bpy.data.objects.remove(obj, do_unlink=True)
path = "/home/afterburner/Projects/unity/FPLab/Tools/Art/SovietWorker/build_worker.py"
exec(compile(open(path).read(), path, "exec"), globals())
print(json.dumps(build_worker()))
