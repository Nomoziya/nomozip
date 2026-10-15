# 包内文件只读预览

0.2.0 开发候选 preview.25 新增；公开正式版仍为 0.1.0。

在解压页选择压缩包，打开“查看内容”，在表格中选中一行，然后点击“预览文件”或按 Ctrl+P。高亮行决定预览文件，左侧勾选框仍只决定“解压所选”的范围。关闭预览返回原列表；Esc 关闭预览，读取期间可取消，关闭内容窗口会取消并等待读取结束。

首期支持：

- 文本：txt、log、md、csv、json、xml、yaml/yml、ini、cfg/conf、html/htm、css、js/ts、cs/cpp/h、py、ps1、bat、sh。HTML、脚本和 Markdown 都按纯文本显示，不执行或渲染其中的脚本、链接和外部资源。
- 编码：严格 UTF-8（可带 BOM），或带 BOM 的 UTF-16/UTF-32 大小端。无法识别或含二进制控制字符时提示使用其它编辑器；不猜测本地 ANSI 编码。
- 图片：PNG、JPEG、BMP、GIF，GIF 仅首帧；按窗口适配且不放大小图。不支持 SVG、WebP、PDF、Office、音视频或外部预览处理器。

文本最多 1 MiB，图片文件最多 8 MiB；图片最多 1600 万像素，单边最多 16384 像素。超过限制时可用原有“解压所选”。限制在服务和专用工作进程入口检查，写入流再次限制实际字节；不将归档头部大小当成唯一限制。固实包仍可能解码同一块中的其它数据，大小限制不等于整包 CPU 或内存配额。

预览会真实解压选中条目到数据目录的 `preview-temp/<本次 GUID>/`，与正常解压目的地分开。完整验证、提取和检查实际内容后读入内存，立即关闭文件并删除本次目录，再显示预览。成功、失败、取消都尝试清理；清理失败显示实际路径，可关闭占用程序后手动删除。无关目录不删除。普通删除不是安全擦除，进程被强杀或断电也可能留下临时文件；加密包的预览内容在读取时为明文。

密码可自动使用库候选或手动输入，手动提示不提供保存选项。预览不写密码库、不更新关联或最近使用、不标记任务已解压、不改变现有结果位置；主密码库沿用当前解锁状态。关闭预览清除视图持有的文本/图片引用，不承诺立即擦除全部托管内存。原始归档在读取期间以只读共享锁打开，旧列表形状变化要求重新浏览。

界面复用现有 WPF、中英资源、浅色/深色/高对比度、固定底栏及焦点规范。文本为可选择、复制的只读控件；图片显示包内路径的可访问名称。没有浏览器或新增运行组件。

实现依据：[Microsoft WPF imaging](https://github.com/dotnet/docs-desktop/blob/main/dotnet-desktop-guide/wpf/graphics-multimedia/imaging-overview.md)、[WPF BitmapImage 源码中的流生命周期](https://source.dot.net/PresentationCore/System/Windows/Media/Imaging/BitmapImage.cs.html)、[严格 UTF8Encoding](https://learn.microsoft.com/en-us/dotnet/api/system.text.utf8encoding.-ctor?view=net-10.0)、[损坏图片解码异常案例](https://github.com/dotnet/wpf/issues/3503)。图片按指定内置编解码器读取，先看尺寸再复制受限像素到独立冻结位图，避免界面持有临时文件句柄。
