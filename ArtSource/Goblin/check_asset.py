import bpy, json, math
from pathlib import Path
root=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(root/'Mossfang_Goblin.blend'))
rig=bpy.data.objects['Goblin_Rig'];mesh=bpy.data.objects['Goblin_Mesh'];scene=bpy.context.scene
sums=[sum(g.weight for g in v.groups) for v in mesh.data.vertices]
report={'weighted_vertices':len(sums),'unweighted':sum(w<=0 for w in sums),'max_weight_sum_error':max(abs(w-1) for w in sums),'max_influences':max(len(v.groups) for v in mesh.data.vertices),'materials':len(mesh.data.materials)}
rig.animation_data.action=bpy.data.actions['Goblin_Walk'];scene.frame_set(1)
first={b.name:b.matrix.copy() for b in rig.pose.bones};scene.frame_set(33)
report['walk_loop_max_matrix_error']=max(abs(first[b.name][i][j]-b.matrix[i][j]) for b in rig.pose.bones for i in range(4) for j in range(4))
report['root_max_displacement']=0
for kind,end in [('Walk',33),('Attack',46)]:
 rig.animation_data.action=bpy.data.actions['Goblin_'+kind]
 for f in range(1,end+1):
  scene.frame_set(f)
  report['root_max_displacement']=max(report['root_max_displacement'],rig.pose.bones['root'].matrix.translation.length)
  assert all(math.isfinite(b.matrix[i][j]) for b in rig.pose.bones for i in range(4) for j in range(4))
assert report['unweighted']==0 and report['max_weight_sum_error']<1e-5
assert report['walk_loop_max_matrix_error']<1e-4
assert report['root_max_displacement']<1e-5
report['fbx_roundtrip']=[]
for name in ['Model','Walk','Attack']:
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 before=set(bpy.data.actions)
 bpy.ops.import_scene.fbx(filepath=str(root.parents[1]/'NetmarbleNeo/Assets/Art/Goblin'/('Goblin_'+name+'.fbx')))
 arms=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
 bodies=[o for o in bpy.context.scene.objects if o.type=='MESH']
 actions=[a for a in bpy.data.actions if a not in before]
 report['fbx_roundtrip'].append({'file':name,'armatures':len(arms),'bones':len(arms[0].data.bones),'meshes':len(bodies),'actions':[{'name':a.name,'range':list(a.frame_range)} for a in actions]})
 assert len(arms)==1 and len(arms[0].data.bones)==18 and len(bodies)==1
 if name!='Model':assert len(actions)==1
(root/'checks.json').write_text(json.dumps(report,indent=2))
print('ASSET_CHECKS',json.dumps(report))
