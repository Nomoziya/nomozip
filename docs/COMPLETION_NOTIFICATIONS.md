# 解压完成通知

在“设置 → 解压完成通知”勾选“整批解压成功后发送系统通知”，点击“保存通知设置”。默认关闭，旧设置没有此字段时保持关闭；设置不能保存时提示本次生效，不覆盖无法识别的未来版本设置。

通知用于解压队列：整批全部任务完成且没有取消时提醒一次，多任务只显示数量；从任务菜单单项解压/失败重试成功后也可提醒。部分失败、取消、空队列不会发成功通知。压缩、归档校验与内容浏览暂不使用此开关。

通知中不包含文件名、输出目录或密码。“已处理”不代表没有跳过同名文件或来源提示，详细结果与提醒仍在窗口中。点击通知或临时通知区域图标会恢复窗口并返回解压页；不会自动运行包内文件或弹出多个 Explorer 窗口。既有“成功后打开目录”设置独立保留，多个结果仍通过一个结果目录窗口选择。

使用 Windows 自带 `Shell_NotifyIconW`，无需额外运行组件或 Windows 签名证书。通知没有声音，使用即时通知标记；系统不能及时展示时可丢弃。遵守 `NIIF_RESPECT_QUIET_TIME`（首次登录/系统升级后的安静时段），这不是对所有 Windows“勿扰”设置行为的保证。系统通知策略可能隐藏提醒，接口接受也不保证可见或保存在通知历史；不重试刷屏。通知点击、隐藏、超时、窗口退出会清理图标，另有 60 秒清理上限。Explorer 重启后不重放旧结果，下次新成功可重新登记。

实现参考 [Microsoft 通知区域指南](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area) 与 [NOTIFYICONDATAW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw)。现代 Windows App SDK 也支持未打包通知；本轮为保持轻量和既有 WPF 技术栈选择系统接口，不引入该依赖。

代理验收使用独立合成数据、真实 26.03/26.04 引擎、真实 WPF/UIA 设置入口与 HWND 回调，替身仅截取系统通知提交以避免在用户桌面弹窗。原生通知图标登记/删除在独立 GitHub Windows VM 验证且不提交可见通知。普通/高对比度、800/1120 DIP 布局截图以及成功/失败/取消、并发单条、单项重试、旧设置/重启、系统拒绝与清理均有报告。实际可见通知投递与用户系统通知策略尚未体验；不把回调测试或图标登记宣称为实际投递通过。


验收记录：本地 63 / CI 65 项通过，[完整 Windows CI 38505385610](https://github.com/Nomoziya/nomozip/actions/runs/38505385610) 的 51 份报告全部通过，新增真实窗口关闭后原 HWND 保留回归；完整证据见 `artifacts/notification-ui-validation.json` 与 `artifacts/notification-ci-review.json`。本地轻量候选 0.2.0-preview.14 为 9745096 字节 / 43 文件，安装器 2277654 字节；四项资产在 `artifacts/release-0.2.0-preview.14/`，固定引擎/worker/公钥/渠道均未变。未发布新正式版或更换实际安装。
