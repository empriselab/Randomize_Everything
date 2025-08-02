"""
Usage:
blender --background --python batch_bake_export.py -- /home/qiandaoliu/rcare_workspace/RCareUnity/Assets/Resources/Chairs /home/qiandaoliu/rcare_workspace/RCareUnity/Assets/Resources
"""
import bpy
import os
import sys

argv = sys.argv
argv = argv[argv.index("--") + 1:]
input_dir = argv[0]
output_dir = argv[1]
texture_size = 1024

def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def bake_texture(obj, image_path):
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)

    mat = obj.active_material
    if not mat or not mat.use_nodes:
        print(f"❌ {obj.name} No Texture")
        return

    nodes = mat.node_tree.nodes
    tex_node = nodes.new('ShaderNodeTexImage')
    tex_image = bpy.data.images.new(name='BakedImage', width=texture_size, height=texture_size)
    tex_node.image = tex_image
    nodes.active = tex_node

    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, use_clear=True)
    tex_image.filepath_raw = image_path
    tex_image.file_format = 'PNG'
    tex_image.save()

    # bind to Base Color
    bsdf = next((n for n in nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf:
        links = mat.node_tree.links
        for l in list(links):
            if l.to_node == bsdf and l.to_socket.name == 'Base Color':
                links.remove(l)
        links.new(tex_node.outputs['Color'], bsdf.inputs['Base Color'])

def export_fbx(filepath):
    bpy.ops.export_scene.fbx(filepath=filepath, embed_textures=True, path_mode='COPY')

# main
for filename in os.listdir(input_dir):
    if filename.lower().endswith(".blend"):
        input_path = os.path.join(input_dir, filename)
        output_name = os.path.splitext(filename)[0]
        output_fbx = os.path.join(output_dir, f"{output_name}.fbx")
        output_png = os.path.join(output_dir, f"{output_name}.png")

        print("{filename}")
        clear_scene()

        print(f"Open .blend: {input_path}")
        bpy.ops.wm.open_mainfile(filepath=input_path)

        bpy.context.scene.render.engine = 'CYCLES'

        for obj in bpy.data.objects:
            if obj.type == 'MESH':
                print(f"Bake: {obj.name}")
                if obj.active_material:
                    bake_texture(obj, output_png)
                else:
                    print(f"Skip {obj.name}: No texture")

        print(f"Export to FBX: {output_fbx}")
        export_fbx(output_fbx)

print("✅ All done.")
