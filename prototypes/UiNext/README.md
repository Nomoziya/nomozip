# nomozip 独立视觉原型

设计方向尚未获确认；此项目不替换产品入口、不连接产品归档/密码库/更新服务。

可体验：真实生成的 8 项 ZIP 样本、搜索、勾选、当前行与预览独立、双击/Ctrl+P 按需预览、收起预览、窗口缩放、浅深主题。窄窗预览为覆盖面板。文件夹进入、真实打开/解压/编辑、其他页面及列表/网格切换将在方向确认后接入，当前点击相关入口会明确说明其原型状态。

原始桌面图标与 PNG 从项目资源直接引用，功能图标来自 Fluent System Icons Regular 20，原 SVG、固定提交、SHA256 和 MIT 许可证位于 Icons/。未添加额外运行组件或第三方控件库。

## 编译与手动预览

在源码工作树根目录使用 .NET 10 SDK：

```powershell
dotnet publish prototypes/UiNext/UiNext.csproj -c Release -o artifacts/ui-next-prototype/app --no-self-contained
```

运行输出目录中的 `nomozip-design-preview.exe`，需 .NET 10 Windows Desktop Runtime。原型只使用内存样本，不保存设置或访问真实密码库。

## 隔离截图

既有 `desktop-ui-validation.yml` 在本独立分支扩展为可选择 `prototype` / `product`；默认只运行此原型，不生成产品成功结论。`--capture` 只允许在隔离 Actions runner 内执行；本机不运行自动截图或桌面验收。

报告记录源码版本、客户区实际 DIP、渲染像素密度、当前实际视觉 DPI、语言/内容/状态和 PNG 的 SHA256。截图由运行中的实际 WPF 客户区生成，包括集成标题栏，不是 AI 图、浏览器仿图或原生桌面 PrintWindow 图。150% 离屏图不代表真实系统缩放或物理显示已验收。

设计取舍与候选参数见 `design-system/next-20261017/DIRECTION.md`，阶段任务和验收记录见 `docs/UI_NEXT_PLAN.md`。方向确认前不把这些候选值标为冻结规范。
