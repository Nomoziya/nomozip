# 解压后的下载来源标记

整包解压、查看内容中的所选解压，以及右键解压会保留入口压缩包已有的 `Zone.Identifier`。没有这项信息的本地包不会被新增标记；多包队列按各包自己的来源处理。文件夹和被跳过的已有文件不变。只读浏览、校验和 SHA-256 不传播标记。

已有信息在开始解压时读取一次，原始字节复制到将交付的暂存文件，不改写 ZoneId 或下载 URL，也不添加本机路径；其它数据流不复制。UTF-8、UTF-16 以及已有的本地/内网/可信/Internet/受限区域值保持原样。复制这项信息不代表文件经过杀毒或一定安全；实际系统提示由 Windows 和其它软件决定。

目标文件系统支持命名流时，标记先写到暂存文件，再沿用原有冲突判断和文件移动。来源无法读取或标记无法写入时，显示错误，不交付本次文件、不保存此次密码使用记录；已存在的文件保留。取消也会清理本次暂存。来源流上限为 64 KiB，超过时明确停止并提示检查来源信息，不静默丢弃。

目标不支持命名流（如 FAT32/exFAT）时，文件仍可解压，窗口明确提示来源信息未保留；需要保留时请选择支持命名流的目录。没有源标记时不出现这种提示。卷能力查询失败时尝试实际流 I/O，不据此误认网络目录没有能力；仅对明确的不支持错误显示上述提示，其它读写错误仍停止交付。

后续覆盖一个文件时，其内容及来源以本次输出为准；跳过则保留已有内容与来源。解压出的内层压缩包也会继承来源，再解压时可以继续传递。分卷以选中的入口文件已有信息为准，不声称合并其它分卷的来源。

本功能借鉴 [Bandizip 的来源标记传播](https://www.bandisoft.com/bandizip/help/zone-identifier/)，使用已有 .NET 文件 I/O 和 Windows 命名流能力，无新增产品运行组件。存储语义见 [微软文件流](https://learn.microsoft.com/en-us/windows/win32/fileio/file-streams)、[Attachment Manager](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-attachmentmanager)。能力查询使用 [GetVolumeInformationByHandleW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationbyhandlew)，其 SMB 限制通过实际 I/O 回退处理；回退逻辑与真实 SMB 环境验收分开记录。

验证仅操作独立合成文件/密码库。NTFS 使用真实 ADS 及官方两版引擎的加密 ZIP/7z，覆盖内容字节、ZoneId/编码、整包/所选、覆盖/跳过、快照、取消、锁定读取与提交前错误；实际界面从浏览、校验、整包和所选入口执行。exFAT 验收脚本只允许临时 GitHub 托管 Windows VM，创建新 64 MiB VHD，严格核对图像关联、磁盘编号/大小/身份后格式化，结束卸载，不运行在用户电脑上。F23 的实际不同 DPI 多屏验收仍独立保留。
