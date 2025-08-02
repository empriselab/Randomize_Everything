"""
Usage:
cd RCareUnity/Assets/RCareCommon/Foundation\ Scripts/
blender --background --python split_blend_objects.py -- /home/qiandaoliu/Downloads/A1\(资产库\)/椅子.blend /home/qiandaoliu/rcare_workspace/RCareUnity/Assets/Resources
"""

import bpy
import os
import sys

# 获取参数
argv = sys.argv
argv = argv[argv.index("--") + 1:]
input_file = argv[0]
output_dir = argv[1]

# 打开原始 .blend 文件
bpy.ops.wm.open_mainfile(filepath=input_file)

# 获取所有对象名字（避免引用被移除）
object_names = [obj.name for obj in bpy.data.objects if obj.type == 'MESH']

for name in object_names:
    # 打开一次原文件（每次都打开干净的）
    bpy.ops.wm.open_mainfile(filepath=input_file)

    # 取消全选，选中目标对象
    bpy.ops.object.select_all(action='DESELECT')
    obj = bpy.data.objects.get(name)

    if obj is None:
        print(f"❌ 对象 {name} 未找到")
        continue

    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    # 删除其他对象
    for other in bpy.data.objects:
        if other.name != name:
            other.select_set(True)
        else:
            other.select_set(False)
    bpy.ops.object.delete()

    # 保存新文件
    safe_name = name.replace(" ", "_").replace("/", "_")
    output_path = os.path.join(output_dir, f"{safe_name}.blend")
    bpy.ops.wm.save_as_mainfile(filepath=output_path)
    print(f"✅ 导出: {output_path}")
