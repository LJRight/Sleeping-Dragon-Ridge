import bpy, json
from pathlib import Path
root=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(root/'Mossfang_Goblin.blend'))
scene=bpy.context.scene;rig=bpy.data.objects['Goblin_Rig']
scene.render.resolution_x=480;scene.render.resolution_y=480;scene.cycles.samples=12
for mode,end in [('Walk',32),('Attack',46)]:
 rig.animation_data.action=bpy.data.actions['Goblin_'+mode]
 folder=root/'previews'/mode.lower();folder.mkdir(exist_ok=True)
 for f in range(1,end+1):
  scene.frame_set(f);scene.render.filepath=str(folder/f'{f:03d}.png');bpy.ops.render.render(write_still=True)
print('MOTION_PREVIEWS_COMPLETE')
