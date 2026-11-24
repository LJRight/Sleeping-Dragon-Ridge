"""Original procedural asset: model, weighted skeleton and authored motion, no downloaded assets."""
import bpy, math, json, os
from mathutils import Vector, Matrix, Quaternion
from pathlib import Path
ROOT=Path(__file__).resolve().parent
OUT=ROOT.parents[1]/'NetmarbleNeo/Assets/Art/Goblin'
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for data in list(bpy.data.actions): bpy.data.actions.remove(data)
scene=bpy.context.scene
scene.render.fps=30
scene.unit_settings.system='METRIC'
materials={}
def mat(name,color,metal=0,rough=.7):
 m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
 p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
 materials[name]=m; return m
skin=mat('Moss skin',(.23,.38,.12)); darkskin=mat('Ear and nose shadow',(.12,.22,.075)); leather=mat('Oxhide leather',(.19,.075,.035)); cloth=mat('Oxblood cloth',(.30,.045,.025)); iron=mat('Weathered iron',(.23,.29,.28),.7,.48); edge=mat('Axe edge',(.48,.54,.47),.8,.35); ivory=mat('Ivory',(.83,.73,.44)); black=mat('Deep brown',(.028,.018,.009)); amber=mat('Amber eyes',(.9,.43,.035),.05,.4)
meshes=[]
def mesh(name,verts,faces,material,bone,weights=None):
 me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update();o=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(o);o.data.materials.append(material)
 if weights:
  for i,entries in enumerate(weights):
   for bn,w in entries.items():
    vg=o.vertex_groups.get(bn) or o.vertex_groups.new(name=bn);vg.add([i],w,'REPLACE')
 else:
  vg=o.vertex_groups.new(name=bone);vg.add(list(range(len(verts))),1,'REPLACE')
 meshes.append(o);return o

def ellipsoid(name,center,scale,material,bone,segments=10,rings=6):
 verts=[];faces=[]
 for j in range(rings+1):
  a=math.pi*j/rings
  for i in range(segments):
   t=2*math.pi*i/segments;verts.append((center[0]+scale[0]*math.sin(a)*math.cos(t),center[1]+scale[1]*math.sin(a)*math.sin(t),center[2]+scale[2]*math.cos(a)))
 for j in range(rings):
  for i in range(segments):
   n=(i+1)%segments;faces.append((j*segments+i,j*segments+n,(j+1)*segments+n,(j+1)*segments+i))
 return mesh(name,verts,faces,material,bone)

def tube(name,points,radii,material,bone,segments=8,weights=None):
 verts=[];faces=[];vw=[]
 for j,pt in enumerate(points):
  direction=Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])
  direction.normalize();basis=direction.cross(Vector((0,1,0)))
  if basis.length<.01:basis=direction.cross(Vector((1,0,0)))
  basis.normalize();other=direction.cross(basis).normalized()
  radius=radii[j];rx,ry=radius if isinstance(radius,tuple) else (radius,radius)
  for i in range(segments):
   a=i*2*math.pi/segments;v=Vector(pt)+basis*math.cos(a)*rx+other*math.sin(a)*ry;verts.append(tuple(v))
   if weights:vw.append(weights[j])
 for j in range(len(points)-1):
  for i in range(segments):
   n=(i+1)%segments;faces.append((j*segments+i,j*segments+n,(j+1)*segments+n,(j+1)*segments+i))
 faces.extend([tuple(reversed(range(segments))),tuple((len(points)-1)*segments+i for i in range(segments))])
 return mesh(name,verts,faces,material,bone,vw if weights else None)

def box(name,center,size,material,bone,rotation=None):
 verts=[]
 rot=rotation or Matrix.Identity(3)
 for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]:
  verts.append(tuple(Vector(center)+rot@Vector((x*size[0]/2,y*size[1]/2,z*size[2]/2))))
 return mesh(name,verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],material,bone)

# Coordinates: metres, Z up, front -Y. Model origin lies on the ground.
rest={};parents={}
def bone(name,head,tail,parent=None):rest[name]=(Vector(head),Vector(tail));parents[name]=parent
bone('root',(0,0,0),(0,0,.18))
bone('pelvis',(0,.035,.80),(0,.025,1.01),'root')
bone('spine',(0,.025,1.01),(0,-.015,1.21),'pelvis')
bone('chest',(0,-.015,1.21),(0,-.03,1.34),'spine')
bone('neck',(0,-.03,1.34),(0,-.065,1.43),'chest')
bone('head',(0,-.065,1.43),(0,-.065,1.78),'neck')
for side,s in [('L',-1),('R',1)]:
 bone('thigh.'+side,(s*.145,.025,.82),(s*.19,-.055,.45),'pelvis')
 bone('shin.'+side,(s*.19,-.055,.45),(s*.20,.02,.13),'thigh.'+side)
 bone('foot.'+side,(s*.20,.02,.13),(s*.20,-.20,.09),'shin.'+side)
 bone('upper_arm.'+side,(s*.28,-.015,1.28),(s*.43,-.025,1.04),'chest')
 bone('forearm.'+side,(s*.43,-.025,1.04),(s*.49,-.10,.83),'upper_arm.'+side)
 bone('hand.'+side,(s*.49,-.10,.83),(s*.51,-.16,.73),'forearm.'+side)

# Torso has blended weights at the waist and chest, limbs share rings across joints.
tube('Body',[(0,.035,.78),(0,.045,.91),(0,.025,1.04),(0,0,1.18),(0,-.015,1.29),(0,-.03,1.34)],[(.20,.14),(.24,.165),(.19,.13),(.26,.16),(.30,.145),(.14,.10)],skin,'pelvis',10,[{'pelvis':1},{'pelvis':1},{'pelvis':.3,'spine':.7},{'spine':.45,'chest':.55},{'chest':1},{'chest':.6,'neck':.4}])
tube('Neck',[(0,-.03,1.30),(0,-.065,1.44)],[.09,.115],skin,'neck')
ellipsoid('Head',(0,-.08,1.59),(.235,.19,.265),skin,'head',12,8)
ellipsoid('Jaw',(0,-.17,1.46),(.17,.115,.105),skin,'head')
ellipsoid('Muzzle',(0,-.225,1.51),(.14,.085,.065),darkskin,'head')
ellipsoid('Nose',(0,-.275,1.60),(.073,.095,.095),skin,'head',8,5)
for side,s in [('L',-1),('R',1)]:
 # Broad pointed ears, not spheres.
 verts=[(s*.17,-.07,1.69),(s*.47,.015,1.77),(s*.36,-.025,1.58),(s*.20,-.095,1.55),(s*.26,.015,1.64)]
 mesh('Ear.'+side,verts,[(0,1,2,3),(0,4,1),(1,4,2),(2,4,3),(3,4,0)],skin,'head')
 mesh('Ear inset.'+side,[(s*.22,-.079,1.66),(s*.415,-.003,1.73),(s*.31,-.045,1.60)],[(0,1,2)],darkskin,'head')
 ellipsoid('Eye socket.'+side,(s*.101,-.238,1.665),(.094,.034,.061),black,'head',8,4)
 ellipsoid('Eye.'+side,(s*.101,-.264,1.665),(.060,.022,.033),amber,'head',8,4)
 ellipsoid('Pupil.'+side,(s*.10,-.283,1.664),(.014,.009,.026),black,'head',6,4)
 box('Heavy brow.'+side,(s*.10,-.251,1.713),(.18,.067,.045),skin,'head',Matrix.Rotation(-s*.25,3,'Y'))
 tube('Tusk.'+side,[(s*.10,-.272,1.46),(s*.112,-.295,1.50),(s*.105,-.29,1.54)],[.024,.017,.002],ivory,'head',6)
 # Stylized limbs with deliberate elbow/knee deformation blends.
 h,k=rest['thigh.'+side];_,a=rest['shin.'+side]
 tube('Leg.'+side,[tuple(h),tuple(h.lerp(k,.35)),tuple(k),tuple(k.lerp(a,.55)),tuple(a)],[.12,.14,.105,.09,.067],skin,'thigh.'+side,8,[{'thigh.'+side:1},{'thigh.'+side:1},{'thigh.'+side:.5,'shin.'+side:.5},{'shin.'+side:1},{'shin.'+side:1}])
 ellipsoid('Boot.'+side,(s*.20,-.08,.095),(.105,.21,.095),leather,'foot.'+side,8,4)
 tube('Shin wrap.'+side,[tuple(a+Vector((0,0,.03))),tuple(a+Vector((0,0,.19)))],[.082,.10],leather,'shin.'+side)
 tube('Boot rim.'+side,[tuple(a+Vector((0,0,.17))),tuple(a+Vector((0,0,.20)))],[.109,.11],iron,'shin.'+side)
 sh,el=rest['upper_arm.'+side];_,wr=rest['forearm.'+side]
 tube('Arm.'+side,[tuple(sh),tuple(sh.lerp(el,.4)),tuple(el),tuple(el.lerp(wr,.55)),tuple(wr)],[.12,.125,.085,.095,.065],skin,'upper_arm.'+side,8,[{'upper_arm.'+side:1},{'upper_arm.'+side:1},{'upper_arm.'+side:.5,'forearm.'+side:.5},{'forearm.'+side:1},{'forearm.'+side:1}])
 ellipsoid('Fist.'+side,tuple(wr+Vector((s*.014,-.035,-.06))),(.088,.073,.10),skin,'hand.'+side,8,5)
 ellipsoid('Thumb.'+side,tuple(wr+Vector((-s*.05,-.07,-.035))),(.035,.04,.055),skin,'hand.'+side,6,4)
 tube('Wrist bracer.'+side,[tuple(el.lerp(wr,.52)),tuple(el.lerp(wr,.95))],[.104,.078],leather,'forearm.'+side)
 for off in [0,.045]:
  box('Bracer rivet.'+side,(s*.50,-.09,.90+off),(.025,.035,.025),iron,'forearm.'+side)
# Leather skirt panels preserve a broad, readable outline.
for i in range(8):
 angle=2*math.pi*i/8; width=.31; low=.64 if i%2==0 else .70
 verts=[]
 for z,r in [(.91,.245),(low,.29)]:
  for da in [-width,width]:verts.append((math.cos(angle+da)*r,math.sin(angle+da)*r*.66,z))
 mesh('Skirt panel %02d'%i,verts,[(0,1,3,2)],cloth if i%2==0 else leather,'pelvis')
tube('Belt',[(0,.035,.90),(0,.035,.96)],[(.25,.18),(.245,.175)],leather,'pelvis',12)
box('Buckle',(0,-.151,.93),(.09,.035,.075),iron,'pelvis')
box('Buckle inset',(0,-.173,.93),(.045,.012,.035),black,'pelvis')
# Diagonal chest strap built as a solid flat band.
mesh('Chest strap',[(-.22,-.155,1.29),(-.14,-.18,1.33),(.15,-.143,1.02),(.09,-.145,.98),(-.22,-.13,1.29),(-.14,-.15,1.33),(.15,-.12,1.02),(.09,-.12,.98)],[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],leather,'chest')
ellipsoid('Left pauldron',(-.31,0,1.31),(.18,.18,.10),iron,'upper_arm.L',8,4)
for i in [-1,0,1]:
 tube('Pauldron spike', [(-.31+i*.09,0,1.37),(-.32+i*.11,0,1.49)],[.03,.001],ivory,'upper_arm.L',5)
# Axe gripped in the right hand; all vertices follow the hand bone.
tube('Axe haft',[(.51,-.13,.57),(.51,-.13,1.16)],[.025,.024],leather,'hand.R',8)
tube('Axe grip',[(.51,-.13,.71),(.51,-.13,.86)],[.034,.034],black,'hand.R',8)
# Wedge silhouette: front of axe blade points toward -Y.
profile=[(-.12,1.12),(-.28,1.22),(-.46,1.17),(-.49,.97),(-.36,.91),(-.25,.99),(-.12,1.01)]
verts=[(.51+x,y,z) for x in [-.032,.032] for y,z in profile];n=len(profile)
faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
mesh('Axe iron head',verts,faces,iron,'hand.R')
mesh('Axe sharpened edge',[(.475,-.46,1.17),(.545,-.46,1.17),(.545,-.49,.97),(.475,-.49,.97),(.51,-.505,.98),(.51,-.475,1.17)],[(0,1,5),(1,2,4,5),(2,3,4),(3,0,5,4)],edge,'hand.R')
# Join render geometry for one skinned mesh, keeping material slots and vertex groups.
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join();body=bpy.context.object;body.name='Goblin_Mesh'
# Flat-color palette atlas: one skinned mesh / one material for mobile rendering.
OUT.mkdir(parents=True,exist_ok=True)
palette=bpy.data.images.new('Goblin_Palette',width=64,height=64,alpha=False)
colors=[tuple(m.diffuse_color) for m in body.data.materials]
pixels=[]
for y in range(64):
 for x in range(64):
  slot=(y//16)*4+x//16
  pixels.extend(colors[slot] if slot<len(colors) else (0.1,0.1,0.1,1))
palette.pixels=pixels;palette.filepath_raw=str(OUT/'Goblin_Palette.png');palette.file_format='PNG';palette.save();palette.pack()
uv=body.data.uv_layers.new(name='PaletteUV')
for face in body.data.polygons:
 index=face.material_index;coord=((index%4+.5)/4,(index//4+.5)/4)
 for loop in face.loop_indices:uv.data[loop].uv=coord
palette_mat=mat('Goblin_Palette_Material',(1,1,1),.08,.72)
tex=palette_mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=palette;tex.interpolation='Closest'
palette_mat.node_tree.links.new(tex.outputs['Color'],palette_mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
body.data.materials.clear();body.data.materials.append(palette_mat)
for face in body.data.polygons:face.material_index=0
# Armature with deform bones only: 
# Portable generic rig, no runtime IK dependency.
armdata=bpy.data.armatures.new('Goblin_Skeleton');rig=bpy.data.objects.new('Goblin_Rig',armdata);bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;body.select_set(False);rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
for name,(h,t) in rest.items():
 b=armdata.edit_bones.new(name);b.head=h;b.tail=t
 if parents[name]:b.parent=armdata.edit_bones[parents[name]]
bpy.ops.object.mode_set(mode='OBJECT');rig.show_in_front=True
body.parent=rig;mod=body.modifiers.new('Skin','ARMATURE');mod.object=rig
for b in rig.pose.bones:b.rotation_mode='QUATERNION'
# IK solved at authoring time. Baked FK tracks retain foot planting in exported animation.
def solve(a,c,l1,l2,pole):
 a,c=Vector(a),Vector(c);d=c-a;dist=min(max(d.length,.001),l1+l2-.001);axis=d.normalized()
 x=(l1*l1-l2*l2+dist*dist)/(2*dist);height=math.sqrt(max(0,l1*l1-x*x))
 p=Vector(pole)-a;side=(p-axis*p.dot(axis)).normalized();return a+axis*x+side*height
lengths={n:(t-h).length for n,(h,t) in rest.items()}
def lerp(a,b,t):return Vector(a).lerp(Vector(b),t)
def smooth(t):return t*t*(3-2*t)
def curve(keys,t):
 for i in range(len(keys)-1):
  ta,va=keys[i];tb,vb=keys[i+1]
  if t<=tb:return lerp(va,vb,smooth(max(0,(t-ta)/(tb-ta))))
 return Vector(keys[-1][1])
def pose(frame,mode,total):
 t=(frame-1)/total;phase=t*2*math.pi
 if mode=='Walk':
  hip=Vector((.022*math.cos(phase),.035,.76+.01*(1-math.cos(phase*2))))
  twist=.065*math.sin(phase);lean=.07
 else:
  hip=curve([(0,(0,.035,.80)),(.26,(.045,.10,.76)),(.43,(-.025,-.12,.76)),(.59,(-.04,-.13,.74)),(1,(0,.035,.80))],t)
  twist=curve([(0,(0,0,0)),(.26,(0,0,-.48)),(.43,(0,0,.50)),(.61,(0,0,.34)),(1,(0,0,0))],t).z
  lean=curve([(0,(0,0,.03)),(.26,(0,0,-.08)),(.43,(0,0,.27)),(.65,(0,0,.19)),(1,(0,0,.03))],t).z
 rot=Matrix.Rotation(twist,3,'Z') @ Matrix.Rotation(lean,3,'X')
 spine=hip+rot@Vector((0,-.01,.21)); chest=spine+rot@Vector((0,-.04,.20)); neck=chest+rot@Vector((0,-.015,.13)); head=neck+Vector((0,-.035,.09))
 targets={'root':(Vector((0,0,0)),Vector((0,0,.18))),'pelvis':(hip,spine),'spine':(spine,chest),'chest':(chest,neck),'neck':(neck,head),'head':(head,head+Vector((0,-.015,.35)))}
 for side,s in [('L',-1),('R',1)]:
  if mode=='Walk':
   u=(t+(0 if side=='L' else .5))%1
   # Stance traverses backward at constant speed; swing lifts and returns the foot.
   if u<.6:fy=-.18+.36*(u/.6);fz=.13;pitch=0
   else:
    q=(u-.6)/.4;fy=.18-.36*smooth(q);fz=.13+.105*math.sin(math.pi*q);pitch=.15*math.sin(math.pi*q)
   ankle=Vector((s*.20,fy,fz));footend=ankle+Vector((0,-.22,.0 if fz>.14 else -.04));footend.z+=pitch*.10
   sh=chest+rot@Vector((s*.28,0,.07))
   armphase=math.sin(phase+(0 if side=='R' else math.pi))
   wrist=sh+Vector((s*.21,-.085+.12*armphase,-.43))
   handend=wrist+Vector((s*.02,-.06,-.10))
  else:
   if side=='L':
    ankle=curve([(0,(-.20,.02,.13)),(.25,(-.20,.02,.13)),(.34,(-.23,-.12,.22)),(.43,(-.25,-.27,.13)),(.68,(-.25,-.27,.13)),(.83,(-.22,-.10,.21)),(1,(-.20,.02,.13))],t)
   else:ankle=Vector((.20,.02,.13))
   footend=ankle+Vector((0,-.22,-.04))
   sh=chest+rot@Vector((s*.28,0,.07))
   if side=='R':
    wrist=curve([(0,(.49,-.10,.83)),(.12,(.55,-.02,1.05)),(.27,(.40,.08,1.65)),(.34,(.43,-.05,1.67)),(.45,(.24,-.61,1.00)),(.54,(-.02,-.58,.78)),(.65,(.09,-.43,.85)),(.85,(.42,-.18,.90)),(1,(.49,-.10,.83))],t)
    handdir=curve([(0,(.02,-.06,-.10)),(.27,(.01,.02,-.11)),(.34,(0,.01,-.11)),(.45,(.01,.11,-.02)),(.56,(-.04,.10,.025)),(.72,(.02,.04,-.10)),(1,(.02,-.06,-.10))],t)
    handend=wrist+handdir
   else:
    wrist=sh+curve([(0,(-.21,-.085,-.43)),(.27,(-.18,-.24,-.20)),(.45,(-.25,.12,-.25)),(.65,(-.20,-.04,-.32)),(1,(-.21,-.085,-.43))],t)
    handend=wrist+Vector((-.02,-.06,-.10))
  thigh=hip+Vector((s*.145,-.01,.02));knee=solve(thigh,ankle,lengths['thigh.'+side],lengths['shin.'+side],thigh+Vector((0,-1,-.3)))
  # Clamp wrist reach so upper and lower arm keep their rest lengths.
  delta=wrist-sh;reach=lengths['upper_arm.'+side]+lengths['forearm.'+side]-.002
  if delta.length>reach:
   correction=sh+delta.normalized()*reach-wrist;wrist+=correction;handend+=correction
  elbow=solve(sh,wrist,lengths['upper_arm.'+side],lengths['forearm.'+side],sh+Vector((s*.7,.5,-.2)))
  targets.update({'thigh.'+side:(thigh,knee),'shin.'+side:(knee,ankle),'foot.'+side:(ankle,footend),'upper_arm.'+side:(sh,elbow),'forearm.'+side:(elbow,wrist),'hand.'+side:(wrist,handend)})
 for name,(h,tail) in targets.items():
  p=rig.pose.bones[name];rh,rt=rest[name]
  q=(rt-rh).normalized().rotation_difference((tail-h).normalized())
  orient=q.to_matrix().to_4x4() @ rig.data.bones[name].matrix_local.to_quaternion().to_matrix().to_4x4()
  p.matrix=Matrix.Translation(h)@orient
  bpy.context.view_layer.update()
  # Keep quaternion signs continuous for stable interpolation.
  if name in previous and p.rotation_quaternion.dot(previous[name])<0:p.rotation_quaternion.negate()
  previous[name]=p.rotation_quaternion.copy()
  p.keyframe_insert('location',frame=frame,group=name);p.keyframe_insert('rotation_quaternion',frame=frame,group=name);p.keyframe_insert('scale',frame=frame,group=name)

rig.animation_data_create();actions={}
for name,total in [('Walk',32),('Attack',45)]:
 action=bpy.data.actions.new('Goblin_'+name);action.use_fake_user=True;rig.animation_data.action=action;previous={}
 for f in range(1,total+2):pose(f,name,total)
 actions[name]=action
 action['description']='Original authored '+name+'; in place, server-driven root.'
 action['duration_seconds']=total/30
# Clean metric rig and export each clip explicitly (avoids exporting unwanted actions).
rig.animation_data.action=actions['Walk'];scene.frame_start=1;scene.frame_end=33;scene.frame_set(1)
OUT.mkdir(parents=True,exist_ok=True)
def export(name,action,end):
 rig.animation_data.action=action;scene.frame_start=1;scene.frame_end=end;scene.frame_set(1)
 bpy.ops.object.select_all(action='DESELECT');body.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
 bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,add_leaf_bones=False,use_armature_deform_only=True,bake_anim=action is not None,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0.0,bake_anim_step=1.0,path_mode='AUTO')
export('Goblin_Walk.fbx',actions['Walk'],33)
export('Goblin_Attack.fbx',actions['Attack'],46)
rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
export('Goblin_Model.fbx',None,1)
# Render stage, separate from selected export objects.
floor=mat('Preview floor',(.045,.06,.055),.05,.75)
bpy.ops.mesh.primitive_cylinder_add(vertices=64,radius=1.35,depth=.08,location=(0,0,-.06));stage=bpy.context.object;stage.name='PREVIEW_Platform';stage.data.materials.append(floor)
world=bpy.data.worlds.new('Studio') if not bpy.data.worlds else bpy.data.worlds[0];scene.world=world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.055,.075,.07,1);world.node_tree.nodes['Background'].inputs[1].default_value=.45
for name,loc,power,size,col in [('Key',(-3,-4,5),650,4,(1,.84,.65)),('Fill',(4,-2,3),450,3,(.62,.82,1)),('Rim',(0,3,4),900,3,(.80,1,.7))]:
 data=bpy.data.lights.new(name,'AREA');o=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(o);o.location=loc;data.energy=power;data.shape='DISK';data.size=size;data.color=col;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('PreviewCamera');cam=bpy.data.objects.new('PreviewCamera',data);bpy.context.collection.objects.link(cam);cam.location=(3.2,-5.6,2.9);cam.rotation_euler=(Vector((0,-.04,.95))-cam.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=2.75;scene.camera=cam
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=640;scene.render.resolution_y=640;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.view_settings.view_transform='AgX'
rig.animation_data.action=actions['Walk'];scene.frame_start=1;scene.frame_end=33;scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Mossfang_Goblin.blend'))
body.data.calc_loop_triangles()
report={'name':'Mossfang Goblin','vertices':len(body.data.vertices),'triangles':len(body.data.loop_triangles),'bones':len(rig.data.bones),'materials':len(body.data.materials),'walk_frames':[1,33],'walk_seconds':32/30,'walk_reference_speed_mps':.36/(.6*32/30),'attack_frames':[1,46],'attack_seconds':1.5,'attack_contact_frame':21,'exports':['Goblin_Model.fbx','Goblin_Walk.fbx','Goblin_Attack.fbx'],'source':'Original procedural modeling and authored skeletal keyframes; no third-party model, texture or motion.'}
(ROOT/'asset_report.json').write_text(json.dumps(report,indent=2))
for mode,frames in [('Walk',[1,9,17,25]),('Attack',[1,13,18,21,26,37,46])]:
 rig.animation_data.action=actions[mode]
 for f in frames:
  scene.frame_set(f);scene.render.filepath=str(ROOT/'previews'/f'{mode}_{f:03d}.png');bpy.ops.render.render(write_still=True)
print('GOBLIN_COMPLETE',json.dumps(report))
