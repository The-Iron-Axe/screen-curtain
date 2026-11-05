# 屏幕帘幕 ScreenCurtain

Windows 屏幕遮罩工具，可通过矩形、多边形、圆形等选区遮挡屏幕的指定区域。

支持独立的颜色、不透明度和边框设置，鼠标穿透，顶点编辑与整体移动，以及坐标导入和导出。程序可最小化到任务栏或关闭到托盘。

## 使用

从发布页面下载单文件 EXE 并运行。程序首次启动会在当前用户的本地应用数据目录解压运行组件，之后可复用缓存；选区和设置也保存在本地。无需单独安装 .NET 或 WinUI 运行库。当前发布构建为 Windows x64，尚未在全新 Windows 环境中完成兼容性验证。

## 从源码构建

需要 Windows x64、.NET 8 SDK、Python 3（包含标准库 `lzma`），以及 Windows 中的 .NET Framework 4 编译器。构建需能还原项目使用的 NuGet 依赖。

在仓库根目录运行：

```powershell
python work/build_extreme.py
```

脚本会发布自包含组件并打包，生成 `work/ScreenCurtain-build.exe`。首次构建会还原依赖；不必手动维护发布组件清单。打包配置位于 `work/compression-profile.json`，依赖的 XZ 解码组件及许可证位于 `work/CompressionVendor/xz-5.6.4`。

重新比较压缩方案可运行 `python work/package_extreme.py --benchmark`，该操作耗时并使用较多临时磁盘空间。

