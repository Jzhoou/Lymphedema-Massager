import os

# 源码目录 & 输出路径
code_dir = r"D:\study\clygz\updown\main"
output_file = r"D:\study\clygz\updown\main\code_output.txt"

suffix = (".cpp", ".h", ".ino")

# 输出文件统一UTF-8，记事本不乱码
with open(output_file, "w", encoding="utf-8-sig") as f_out:

    for filename in os.listdir(code_dir):
        if filename.lower().endswith(suffix):
            full_path = os.path.join(code_dir, filename)

            try:
                # 先试GBK（Arduino默认编码），不行再UTF8
                try:
                    with open(full_path, "r", encoding="gbk") as f:
                        code = f.read()
                except:
                    with open(full_path, "r", encoding="utf-8") as f:
                        code = f.read()

                # 写入格式
                f_out.write(f"===== {filename} =====\n")
                f_out.write(code)
                f_out.write("\n\n" + "-"*80 + "\n\n")
                print(f"成功: {filename}")

            except Exception as e:
                print(f"跳过 {filename} : {e}")

print("\n全部导出完成，中文注释完全正常！")