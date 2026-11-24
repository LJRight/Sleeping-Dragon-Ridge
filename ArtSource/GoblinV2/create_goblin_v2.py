"""Original static goblin study. Blender geometry only; no external assets or rig."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector
P=Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
S=bpy.context.scene;S.unit_settings.system='METRIC'
parts=[]
def mat(n,c,metal=0,rough=.6,noise=0):
 m=bpy.data.materials.new(n);m.diffuse_color=(*c,1);m.use_nodes=True
 nt=m.node_tree;p=nt.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*c,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
 if noise:
  t=nt.nodes.new('ShaderNodeTexNoise');t.inputs['Scale'].default_value=75;t.inputs['Detail'].default_value=2
  b=nt.nodes.new('ShaderNodeBump');b.inputs['Strength'].default_value=.16;b.inputs['Distance'].default_value=noise;nt.links.new(t.outputs['Fac'],b.inputs['Height']);nt.links.new(b.outputs['Normal'],p.inputs['Normal'])
 return m
skin=mat('01 | olive jade skin',(.19,.29,.095),rough=.62,noise=.0015)
inner=mat('02 | warm ear cartilage',(.22,.20,.095),rough=.8)
dark=mat('03 | deep creases',(.035,.046,.017),rough=.85)
lip=mat('04 | lip olive',(.105,.14,.04),rough=.72)
bone=mat('05 | aged ivory',(.73,.61,.36),rough=.44)
eye=mat('06 | amber sclera',(.8,.42,.055),rough=.22)
pupil=mat('07 | obsidian',(.008,.013,.005),rough=.17)
leather=mat('08 | oxblood hide',(.13,.045,.026),rough=.72,noise=.012)
trim=mat('09 | leather edges',(.28,.125,.055),rough=.72)
cloth=mat('10 | faded wine cloth',(.17,.025,.035),rough=.95,noise=.009)
steel=mat('11 | dark hammered iron',(.105,.16,.18),.78,.38,.006)
edge=mat('12 | worn iron edges',(.36,.42,.40),.8,.29)
brass=mat('13 | aged brass',(.43,.25,.075),.72,.4)
wood=mat('14 | walnut wood',(.095,.04,.017),rough=.8,noise=.016)
def assign(o,n,m):
 o.name=n;o.data.materials.clear();o.data.materials.append(m);parts.append(o);return o
def ell(n,c,scale,m,rot=None):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,location=c);o=bpy.context.object;o.scale=scale
 if rot:o.rotation_euler=rot
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 for f in o.data.polygons:f.use_smooth=True
 return assign(o,n,m)
def mesh(n,v,f,m,bevel=0):
 d=bpy.data.meshes.new(n);d.from_pydata(v,[],f);d.update();o=bpy.data.objects.new(n,d);bpy.context.collection.objects.link(o);assign(o,n,m)
 if bevel:
  mod=o.modifiers.new('Hand softened edges','BEVEL');mod.width=bevel;mod.segments=2
  bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.modifier_apply(modifier=mod.name);o.select_set(False)
 return o
def tube(n,pts,rs,m,N=12):
 vs=[];fs=[]
 for j,p in enumerate(pts):
  t=(Vector(pts[min(j+1,len(pts)-1)])-Vector(pts[max(0,j-1)])).normalized();u=t.cross(Vector((0,1,0)))
  if u.length<.01:u=t.cross(Vector((1,0,0)))
  u.normalize();v=t.cross(u).normalized();r=rs[j];a,b=r if isinstance(r,tuple) else (r,r)
  for k in range(N):vs.append(Vector(p)+a*math.cos(k*2*math.pi/N)*u+b*math.sin(k*2*math.pi/N)*v)
 for j in range(len(pts)-1):
  for k in range(N):a=j*N+k;b=j*N+(k+1)%N;fs.append((a,b,b+N,a+N))
 fs.extend([tuple(reversed(range(N))),tuple((len(pts)-1)*N+k for k in range(N))]);o=mesh(n,vs,fs,m)
 for f in o.data.polygons:f.use_smooth=True
 return o
def line(n,pts,r,m):
 d=bpy.data.curves.new(n,'CURVE');d.dimensions='3D';d.resolution_u=4;d.bevel_depth=r;d.bevel_resolution=2;s=d.splines.new('BEZIER');s.bezier_points.add(len(pts)-1)
 for b,p in zip(s.bezier_points,pts):b.co=p;b.handle_left_type='VECTOR' if any(k in n for k in ['rim','hem','edge','border','edging']) else 'AUTO';b.handle_right_type=b.handle_left_type
 o=bpy.data.objects.new(n,d);bpy.context.collection.objects.link(o);d.materials.append(m);parts.append(o);return o
def box(n,c,scale,m,bevel=.01,rot=None):
 bpy.ops.mesh.primitive_cube_add(size=1,location=c);o=bpy.context.object;o.scale=scale
 if rot:o.rotation_euler=rot
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);assign(o,n,m)
 if bevel:
  mod=o.modifiers.new('Rounded forged edge','BEVEL');mod.width=bevel;mod.segments=2;bpy.ops.object.modifier_apply(modifier=mod.name)
 return o
def join_sculpt(obs,n,voxel,ratio):
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.select_set(True)
 bpy.context.view_layer.objects.active=obs[0];bpy.ops.object.join();o=bpy.context.object;o.name=n
 for a in obs[1:]:parts.remove(a)
 mod=o.modifiers.new('Fused sculpt volumes','REMESH');mod.mode='VOXEL';mod.voxel_size=voxel;bpy.ops.object.modifier_apply(modifier=mod.name)
 mod=o.modifiers.new('Surface relaxation','SMOOTH');mod.factor=1.2;mod.iterations=5;bpy.ops.object.modifier_apply(modifier=mod.name)
 mod=o.modifiers.new('Sculpt topology reduction','DECIMATE');mod.ratio=ratio;bpy.ops.object.modifier_apply(modifier=mod.name)
 for f in o.data.polygons:f.use_smooth=True
 return o
# A broad hunched torso tapering into thin, bent limbs.
start=len(parts)
ell('ribcage',(0,.055,1.22),(.29,.19,.29),skin)
ell('trapezius hump',(0,.13,1.46),(.30,.19,.17),skin)
ell('belly',(0,-.015,1.02),(.22,.16,.23),skin)
ell('pelvis',(0,.035,.84),(.225,.17,.17),skin)
ell('neck',(0,-.075,1.49),(.14,.15,.20),skin)
for s in [-1,1]:
 ell('deltoid',(s*.295,.015,1.40),(.145,.155,.16),skin)
 tube('upper arm',[(s*.29,.02,1.39),(s*.38,-.005,1.25),(s*.45,-.04,1.10)],[.13,.125,.083],skin)
 ell('bicep',(s*.365,-.035,1.27),(.09,.105,.135),skin)
 tube('forearm',[(s*.45,-.04,1.1),(s*.50,-.09,.98),(s*.54,-.15,.84)],[.082,.105,.062],skin)
 ell('knuckle palm',(s*.55,-.16,.77),(.082,.066,.11),skin)
 tube('thigh',[(s*.13,.035,.86),(s*.19,-.01,.68),(s*.215,-.10,.49)],[.135,.13,.084],skin)
 ell('knee',(s*.215,-.12,.48),(.087,.085,.09),skin)
 tube('shin',[(s*.215,-.10,.48),(s*.225,-.015,.32),(s*.24,.015,.12)],[.077,.082,.05],skin)
 ell('foot',(s*.245,-.085,.085),(.095,.18,.075),skin)
body=join_sculpt(parts[start:].copy(),'BODY | unified organic sculpt',.012,.32)
# Characterful face: forward jaw, cheek planes and a hooked nose fused to skull.
start=len(parts)
ell('cranium',(0,-.10,1.73),(.225,.19,.255),skin)
ell('occiput',(0,-.015,1.73),(.19,.18,.20),skin)
ell('jaw',(0,-.205,1.565),(.185,.135,.12),skin)
ell('lower muzzle',(0,-.29,1.567),(.153,.095,.066),skin)
ell('upper muzzle',(0,-.285,1.635),(.14,.09,.054),skin)
ell('nose bridge',(0,-.267,1.77),(.061,.082,.14),skin,(-.18,0,0))
ell('hook nose',(0,-.357,1.697),(.073,.105,.067),skin)
for s in [-1,1]:
 ell('nose wing',(s*.055,-.336,1.679),(.041,.052,.034),skin)
 ell('high cheek',(s*.16,-.23,1.685),(.077,.064,.092),skin,(0,s*.45,0))
 ell('temple',(s*.17,-.14,1.77),(.075,.09,.12),skin)
 ell('sloping brow',(s*.105,-.266,1.824),(.12,.059,.049),skin,(0,-s*.28,0))
head=join_sculpt(parts[start:].copy(),'HEAD | brow cheek muzzle sculpt',.006,.32)
# Sculpted leaf ears: thick outer ridge, recessed cartilage, irregular outline.
for s in [-1,1]:
 outline=[(.17,-.09,1.82),(.29,-.06,1.88),(.56,.025,1.98),(.49,.025,1.85),(.34,-.025,1.69),(.21,-.09,1.68)]
 front=[(s*x,y,z) for x,y,z in outline];back=[(s*x,y+.045,z) for x,y,z in outline];center=(s*.29,-.048,1.79)
 vs=front+[center]+back+[(s*.29,.03,1.79)];fs=[]
 for i in range(6):j=(i+1)%6;fs += [(i,j,6),(7+j,7+i,13),(i,7+i,7+j,j)]
 mesh('Ear cartilage shell',vs,fs,skin,.009)
 mesh('Inset ear hollow',[(s*.232,-.073,1.80),(s*.48,.012,1.927),(s*.345,-.019,1.739),(s*.25,-.071,1.72)],[(0,1,2,3)],inner)
 line('Ear raised rim',front+[front[0]],.011,skin)
 line('Inner cartilage',[(s*.225,-.103,1.71),(s*.28,-.073,1.76),(s*.40,-.012,1.85)],.013,skin)
 # Deep inset eyes and sharply angled eyelids.
 ell('Eye socket',(s*.105,-.272,1.772),(.083,.033,.052),dark)
 ell('Amber eyeball',(s*.105,-.294,1.772),(.057,.025,.034),eye)
 ell('Vertical pupil',(s*.103,-.318,1.772),(.011,.005,.026),pupil)
 line('Upper eyelid',[(s*.047,-.312,1.777),(s*.10,-.321,1.794),(s*.163,-.282,1.803)],.012,skin)
 line('Lower eyelid',[(s*.047,-.309,1.77),(s*.108,-.315,1.745),(s*.163,-.282,1.78)],.009,skin)
 ell('Nostril',(s*.047,-.399,1.674),(.021,.009,.013),dark)
 line('Nose cheek fold',[(s*.075,-.345,1.674),(s*.11,-.327,1.641),(s*.149,-.298,1.61)],.006,lip)
 tube('Curved lower tusk',[(s*.105,-.343,1.562),(s*.122,-.377,1.60),(s*.125,-.387,1.647),(s*.113,-.378,1.673)],[.023,.019,.011,.001],bone,12)
 # Distinct fingers and pointed nails, not mitten hands.
 for j in range(3):
  x=s*(.495+j*.044);z=.745-abs(j-1)*.006
  tube('Curled finger',[(x,-.18,z),(x,-.21,z-.06),(x,-.18,z-.094)],[.022,.023,.015],skin)
  ell('Finger nail',(x,-.205,z-.078),(.013,.01,.021),bone)
 tube('Opposing thumb',[(s*.49,-.15,.79),(s*.475,-.23,.76),(s*.49,-.25,.727)],[.031,.024,.014],skin)
 for j in range(3):
  x=s*(.185+j*.059)
  ell('Toe',(x,-.233,.072),(.031,.058,.038),skin)
  ell('Toenail',(x,-.271,.083),(.021,.025,.01),bone)
line('Mouth incision',[(-.145,-.322,1.608),(-.08,-.371,1.603),(0,-.381,1.60),(.08,-.371,1.603),(.145,-.322,1.615)],.008,dark)
line('Lower lip',[(-.085,-.37,1.584),(0,-.385,1.581),(.085,-.37,1.584)],.012,lip)
# Brow furrows, one cheek scar and ear piercing add asymmetry.
for x in [-.025,.025]:line('Frown ridge',[(x,-.278,1.815),(x*.8,-.273,1.86),(x*1.2,-.248,1.89)],.008,skin)
line('Old cheek scar',[(.17,-.272,1.724),(.187,-.265,1.69),(.172,-.267,1.655)],.0045,inner)
for z in [1.679,1.695,1.711]:line('Scar seam',[(.167,-.272,z-.004),(.185,-.273,z+.005)],.003,lip)
def torus(n,c,R,r,m,rot=(math.pi/2,0,0)):
 bpy.ops.mesh.primitive_torus_add(major_radius=R,minor_radius=r,major_segments=24,minor_segments=8,location=c,rotation=rot);return assign(bpy.context.object,n,m)
torus('Left ear gold ring',(-.35,-.025,1.73),.036,.008,brass)
# Layered irregular skirt panels: solid thickness, borders and actual stitches.
for i in range(10):
 a=2*math.pi*i/10;v=[]
 for z,r in [(.94,.24),(.80,.265),(.62+(.025 if i%2 else -.02),.295)]:
  for da in [-.27,.27]:v.append((math.cos(a+da)*r,math.sin(a+da)*r*.70+.025,z))
 o=mesh('Overlapping hide panel %02d'%i,v,[(0,1,3,2),(2,3,5,4)],cloth if i%3==0 else leather)
 mod=o.modifiers.new('Leather thickness','SOLIDIFY');mod.thickness=.009
 line('Panel piped hem',[v[0],v[2],v[4],v[5],v[3],v[1]],.005,trim)
 for j in range(4):
  t=(j+1)/6;p=Vector(v[0]).lerp(Vector(v[4]),t);q=p+Vector((.012*math.cos(a+math.pi/2),.012*math.sin(a+math.pi/2),.003));line('Waxed stitches',[p,q],.002,bone)
# Belt bands and a hollow rectangular buckle.
tube('Broad leather belt',[(0,.025,.915),(0,.025,.985)],[(.252,.182),(.246,.18)],leather,32)
for z in [.922,.978]:
 line('Belt edging',[(.25*math.cos(a),.025+.184*math.sin(a),z) for a in [i*math.pi/16 for i in range(33)]],.006,trim)
for x in [-.057,.057]:box('Buckle side',(x,-.173,.952),(.013,.024,.075),brass,.004)
for z in [.918,.987]:box('Buckle bar',(0,-.173,z),(.12,.024,.012),brass,.004)
box('Buckle pin',(0,-.192,.951),(.087,.01,.007),brass,.002)
# Diagonal harness, segmented to follow torso volume.
strap=[(-.25,-.11,1.48),(-.19,-.175,1.38),(-.09,-.19,1.24),(.045,-.18,1.11),(.16,-.125,.99)]
v=[]
for x,y,z in strap:v.extend([(x-.032,y-.014,z-.021),(x+.032,y-.014,z+.021)])
o=mesh('Fitted chest harness',v,[(2*i,2*i+1,2*i+3,2*i+2) for i in range(4)],leather);mod=o.modifiers.new('Hide thickness','SOLIDIFY');mod.thickness=.015
for j in [0,1]:line('Harness border',[v[2*i+j] for i in range(5)],.004,trim)
for x,y,z in strap[1:-1]:ell('Harness rivet',(x,y-.024,z),(.009,.006,.009),brass)
# Forged asymmetric shoulder armor with a rolled border and rivets.
for layer in range(3):
 x=-.32-layer*.035;z=1.48-layer*.075
 outline=[(x+.14,-.13,z),(x-.10,-.17,z-.01),(x-.20,-.07,z-.04),(x-.18,.10,z-.035),(x-.03,.17,z+.015),(x+.12,.10,z+.025)]
 center=(x-.025,0,z+.085);v=outline+[center]+[(a,b,c-.025) for a,b,c in outline];f=[]
 for i in range(6):j=(i+1)%6;f.extend([(i,j,6),(i,7+i,7+j,j)])
 mesh('Layered forged pauldron',v,f,steel,.008);line('Rolled shoulder rim',outline+[outline[0]],.009,edge)
 for idx in [1,2,4]:a,b,c=outline[idx];ell('Armor brass rivet',(a,b-.007,c+.014),(.013,.012,.009),brass)
for x,y,z in [(-.27,.04,1.58),(-.40,0,1.57),(-.51,.02,1.50)]:
 torus('Spike iron collar',(x,y,z),.032,.009,edge,(0,0,0));tube('Curved horn spike',[(x,y,z),(x-.025,y+.015,z+.09),(x-.055,y+.02,z+.16)],[.032,.018,.001],bone)
# Bracers, shin guards, wraps and straps.
for s in [-1,1]:
 tube('Wrist hide bracer',[(s*.49,-.08,1.015),(s*.54,-.145,.86)],[.108,.079],leather,16)
 for z,x,y,r in [(1.005,.49,-.08,.11),(.88,.53,-.14,.084)]:
  tube('Bracer strap',[(s*x,y,z-.014),(s*x,y,z+.014)],[r,r],trim,16)
 for j in range(3):box('Bracer iron splint',(s*(.48+j*.023),-.183,.95),(.023,.018,.13),steel,.006,(0,s*.2,0))
 tube('Calf hide wrap',[(s*.24,.012,.15),(s*.222,-.03,.36)],[.064,.088],leather,16)
 for j in range(4):
  z=.16+j*.05;line('Spiral calf binding',[(s*.23+.08*math.cos(a),-.014+.085*math.sin(a),z+.012*a/(2*math.pi)) for a in [i*math.pi/8 for i in range(17)]],.009,trim)
 outline=[(s*.16,-.105,.37),(s*.22,-.15,.42),(s*.285,-.105,.36),(s*.28,-.09,.18),(s*.23,-.12,.145),(s*.18,-.09,.18)]
 mesh('Kite shin plate',outline+[(s*.22,-.175,.30)],[(i,(i+1)%6,6) for i in range(6)],steel,.004);line('Shin plate edge',outline+[outline[0]],.006,edge)
 box('Sandal sole',(s*.245,-.085,.032),(.20,.34,.035),leather,.025)
 box('Sandal toe strap',(s*.245,-.14,.11),(.20,.055,.035),trim,.01)
# Utility pouch and bone charms.
ell('Belt pouch',(.26,.005,.82),(.075,.105,.095),leather)
box('Pouch flap',(.29,-.08,.85),(.10,.026,.085),trim,.018,(0,.12,-.2));ell('Pouch stud',(.29,-.10,.83),(.012,.007,.012),brass)
for j in range(3):
 x=-.09+j*.065;line('Charm cord',[(x,-.175,.91),(x-.01,-.21,.80)],.004,wood)
 tube('Tooth trophy',[(x-.01,-.21,.80),(x-.015,-.22,.76),(x-.03,-.22,.735)],[.016,.012,.001],bone)
# Right hand holds a visibly forged axe with an inset cutting bevel.
tube('Axe curved haft',[(.56,-.20,.43),(.55,-.22,.76),(.58,-.20,1.18)],[.025,.026,.023],wood,16)
for j in range(10):torus('Axe grip winding',(.552,-.217,.66+j*.014),.028,.004,trim,(0,0,0))
profile=[(-.20,1.18),(-.34,1.26),(-.53,1.28),(-.57,1.20),(-.59,1.12),(-.57,1.025),(-.49,.99),(-.32,1.075),(-.20,1.08)]
v=[(.58+dx,y,z) for dx in [-.035,.035] for y,z in profile];n=len(profile)
mesh('Forged axe head',v,[tuple(reversed(range(n))),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],steel,.008)
for s in [-1,1]:
 strip=[(.58+s*.036,y,z) for y,z in profile[2:7]]+[ (.58+s*.043,y+.035,z) for y,z in reversed(profile[2:7])]
 mesh('Honed cutting bevel',strip,[tuple(range(len(strip)))],edge)
for z in [1.105,1.16]:ell('Axe mounting rivet',(.62,-.235,z),(.012,.012,.012),brass)
# Export static geometry independently from studio. Convert curves/apply modifiers.
bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=body
bpy.ops.object.convert(target='MESH')
model=list(bpy.context.selected_objects)
for o in model:
 for mod in list(o.modifiers):
  bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mod.name)
tri=0;verts=0
for o in model:o.data.calc_loop_triangles();tri+=len(o.data.loop_triangles);verts+=len(o.data.vertices)
bpy.ops.export_scene.fbx(filepath=str(P/'Mossfang_Goblin_V2_Static.fbx'),use_selection=True,object_types={'MESH'},bake_anim=False,axis_forward='-Z',axis_up='Y')
bpy.ops.export_scene.gltf(filepath=str(P/'Mossfang_Goblin_V2_Static.glb'),use_selection=True,export_format='GLB')
(P/'report.json').write_text(json.dumps({'vertices':verts,'triangles':tri,'mesh_objects':len(model),'rig':False,'animations':False,'purpose':'Static visual study; not retopologized or mobile-optimized. Procedural Blender bump nodes do not export to FBX/GLB.'},indent=2))
# Studio
floor=mat('Studio charcoal',(.025,.032,.038),rough=.8)
bpy.ops.mesh.primitive_cylinder_add(vertices=96,radius=.96,depth=.045,location=(0,0,-.026));o=bpy.context.object;o.data.materials.append(floor)
be=o.modifiers.new('Soft stage edge','BEVEL');be.width=.015;be.segments=3
w=bpy.data.worlds.new('Slate studio');S.world=w;w.use_nodes=True;w.node_tree.nodes['Background'].inputs[0].default_value=(.025,.035,.045,1);w.node_tree.nodes['Background'].inputs[1].default_value=.35
for n,loc,pow,size,col in [('Key',(-3,-4,5),500,3,(1,.87,.7)),('Fill',(3,-2,2.7),240,3,(.69,.83,1)),('Rim',(.5,2.2,3.2),600,2,(.75,.90,1))]:
 d=bpy.data.lights.new(n,'AREA');o=bpy.data.objects.new(n,d);S.collection.objects.link(o);o.location=loc;d.energy=pow;d.shape='DISK';d.size=size;d.color=col;o.rotation_euler=(Vector((0,0,1.1))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Art review');cam=bpy.data.objects.new('Art review',d);S.collection.objects.link(cam);S.camera=cam;d.type='ORTHO'
S.render.engine='CYCLES';S.cycles.samples=40;S.cycles.use_denoising=True;S.render.resolution_x=1000;S.render.resolution_y=1100;S.render.resolution_percentage=100;S.view_settings.view_transform='AgX'
def camera(loc,target,scale):cam.location=loc;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();d.ortho_scale=scale
camera((2.8,-5,2.7),(0,-.04,1.02),2.5)
bpy.ops.object.select_all(action='DESELECT');head.select_set(True);bpy.context.view_layer.objects.active=head
bpy.ops.wm.save_as_mainfile(filepath=str(P/'Mossfang_Goblin_V2.blend'))
for name,loc,target,scale in [('Hero',(2.8,-5,2.7),(0,-.04,1.02),2.5),('Face',(1.5,-4,2.1),(0,-.15,1.74),1.12),('Front',(0,-5,1.6),(0,-.04,1.02),2.4),('Back',(-2.7,5,2.5),(0,0,1.02),2.5)]:
 camera(loc,target,scale);S.render.filepath=str(P/'previews'/f'{name}.png');bpy.ops.render.render(write_still=True)
print('V2_COMPLETE',tri,verts)
