import bpy
path='/home/afterburner/Projects/unity/FPLab/Tools/Art/SovietWorker/build_worker.py'
exec(compile(open(path).read(),path,'exec'),globals())
print(render_preview('threequarter'))
print(render_preview('face'))
