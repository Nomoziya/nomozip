# nomozip 命令行 / Command line

在终端中运行安装目录下的 `nomozip-cli.exe`；它会等待任务完成，PowerShell 用 `$LASTEXITCODE`、cmd 用 `%ERRORLEVEL%` 读取退出码。图形界面的 `ArchivePassword.exe` 启动方式保持原样。

Run `nomozip-cli.exe` from the installation directory in a terminal. It waits for completion and returns an exit code to the shell. The GUI entry remains `ArchivePassword.exe`. No extra runtime is bundled or required beyond the product's shared .NET runtime.

```powershell
.\nomozip-cli.exe --help
.\nomozip-cli.exe --version
.\nomozip-cli.exe test "D:\archives\sample.zip"
.\nomozip-cli.exe extract "D:\archives\sample.7z" --output "D:\result" --use-vault
.\nomozip-cli.exe create "D:\files\资料" --output "D:\archives\new.7z" --format 7z
.\nomozip-cli.exe create "D:\files\report.txt" --output "D:\archives\private.7z" --encrypt --interactive
```

首期提供 `extract`、`test`、`create` 和 `vault-list`。一次解压/测试一个归档，创建可接收多个文件或目录；用脚本循环处理多包。创建支持现有 7z/ZIP/TAR/GZIP/XZ，默认 7z/级别 5，级别为 0/1/3/5/7/9。TAR 只打包；GZIP/XZ 只接受一个文件，不能加密；ZIP 新密码仅 ASCII。未提供命令行分卷、自解压、包修改、转换、预览或跨重启续作选项；这些不影响现有图形界面功能。

The initial commands are `extract`, `test`, `create` and `vault-list`. Extract/test accept one archive; create accepts multiple sources. Loop in the shell for multiple archives. Creation supports 7z, ZIP, TAR, GZIP and XZ with the same format restrictions as the GUI. Default: 7z, level 5. This entry does not expose split-volume creation, SFX creation, archive editing/conversion, preview or restart resume options yet.

默认不交互、不自动读取密码库、不覆盖。加密包没有可用密码时立即返回错误。`--interactive` 只在真实终端中启用遮罩密码输入；新建加密包要求确认密码，退格可修改，Esc 取消。标准输入重定向时不得使用该开关。成功与失败都不写密码库记录、关联、最近成功时间或界面设置。

Default: unattended, no vault access and no overwrite. Missing passwords fail immediately. `--interactive` enables masked prompts in a terminal, with confirmation for new archive passwords; Backspace edits, Escape cancels. It cannot be combined with redirected input. Vault records, associations, success timestamps and GUI settings remain read-only.

## 密码来源 / Credentials

- `--password-stdin`：从标准输入读取一行 UTF-8 密码，去掉行尾；EOF/空行返回参数错误。不接受明文密码参数、密码文件参数或环境变量密码选项。请由自己的秘密管理工具向标准输入传送，避免把真实密码写入脚本和 shell 历史。
- `--password-id GUID`：引用一个库记录，可用于解压/测试或 `create --encrypt`。用 `vault-list` 查询编号，该命令仅输出 `id/name/group/autoTry`，不输出密码、备注或来源。
- `--use-vault`：仅解压/测试，按图形界面的关联和优先规则尝试已启用记录；支持包内多个密码。与 `--password-id` 互斥，可与标准输入密码组合，库候选耗尽后使用输入密码。
- `--master-password-stdin`：为上述库访问或 `vault-list` 读取 APV2 主密码；仅在当前进程内解锁，结束后清理派生密钥。不重置、不迁移、不启用/关闭主密码。

`--password-stdin` reads one UTF-8 line; EOF or an empty line is an argument error. `--password-id GUID` references an exact vault record. `--use-vault` enables existing auto-try rules for extraction/testing only. `vault-list` outputs IDs and metadata only. APV2 requires `--master-password-stdin` or an interactive master password; unlocking lasts only for this process.

同时使用两种标准输入开关时，固定第一行为归档密码、第二行为主密码，不受开关先后顺序影响。密码不能包含换行；交互字符串不承诺立即从全部托管内存擦除，主密码派生密钥按现有库实现清理。库仍遵守本 Windows 用户的 DPAPI/APV2 边界。

When both stdin switches are present, line 1 is always the archive password and line 2 the master password, regardless of option order. Passwords cannot contain line breaks. DPAPI/APV2 Windows-user restrictions still apply.

```powershell
.\nomozip-cli.exe vault-list
.\nomozip-cli.exe create "D:\files" --output "D:\private.7z" --encrypt --password-id "00000000-0000-0000-0000-000000000001"
```

示例 GUID 是占位符，必须换成查询得到的记录编号。The example GUID is a placeholder; replace it with an actual record ID.

## 覆盖、取消与引擎 / Overwrite, cancellation and engine

解压默认遇同名文件整次停止，在处理完所有冲突决定前不移动最终文件。`--on-conflict skip` 跳过并返回警告，`replace` 明确替换。创建目标存在时默认失败；`--replace` 允许临时创建并验证后替换。源文件/归档不删除。普通解压提交多个文件不是整目录事务，交付期间取消/文件系统失败时已经交付的文件可能保留；以结果状态为准，不宣称原子整包回退。

Extraction conflicts default to failure; choose `--on-conflict skip` or `replace` explicitly. Creation requires `--replace` to overwrite an existing target after validation. Sources are never deleted. Multi-file extraction is not a whole-directory transaction; cancellation during delivery can leave files already delivered.

Ctrl+C 或 Esc 受控取消。脚本也可用 `--cancel-file PATH`：该文件预先存在或运行期间出现即请求取消，程序不创建/删除该信号文件。每个作业使用独立文件，重跑前由脚本清理。不要用强杀进程代替正常取消和临时文件清理。

Ctrl+C/Escape request cancellation. For automation, `--cancel-file PATH` requests cancellation when that file exists or appears. The CLI neither creates nor deletes it. Use a separate signal file per job.

`--data-dir` 指定数据目录，默认与 GUI 一致；`--engine NN.NN` 指定一个已验证可用版本，仅本次使用。省略时读取相同架构的现用引擎选择，无可用选择时用已验证的最新内置/已安装版本。沿用固定公钥、官方摘要与架构隔离，不加载任意 DLL，不写现用引擎选择或下载安装更新。

`--data-dir` selects a profile, defaulting to the GUI profile. `--engine NN.NN` selects a verified available engine for this invocation only. Otherwise the CLI follows the architecture-specific GUI engine selection. Existing signature/hash verification remains in use; no engine update or selection is saved.

## 脚本接口 / Script contract

stdout 默认只含一个 schema 1 JSON 结果；`--help` 输出说明，`--version` 输出版本 JSON。退出结果字段为 `schema/command/status/code/exitCode/engineVersion/architecture`，业务结果可附 `output/warning/records`。`code` 是稳定的机器标识；不要根据翻译文案判断结果。错误不回显整条命令或原始异常；调用者自己的 shell 回显/历史不受程序控制。

stdout contains one schema-1 JSON result, except human-readable help. `--progress` sends JSON lines to stderr with `kind=progress/state/completed/total`; progress contains no password or raw command line. Scripts should use `code` and `exitCode`, not translated messages. Help/version return 0; JSON field order is not an interface guarantee.

| Exit | 含义 / Meaning |
|---|---|
| 0 | 成功 / Success |
| 1 | 完成但有警告，例如跳过文件或来源标记不支持 / Completed with warnings |
| 2 | 业务失败，包括错密、库锁定、冲突、损坏或 I/O 失败 / Operation failed |
| 7 | 参数或输入方式错误 / Invalid arguments or input mode |
| 8 | 内存不足 / Out of memory |
| 255 | 已请求取消 / Cancelled |

```powershell
$taskExit = 0
foreach ($taskArchive in Get-ChildItem 'D:\archives\*.zip') {
    & '.\nomozip-cli.exe' extract $taskArchive.FullName --output (Join-Path 'D:\result' $taskArchive.BaseName) --use-vault
    if ($LASTEXITCODE -eq 255) { exit 255 }
    if ($LASTEXITCODE -gt $taskExit) { $taskExit = $LASTEXITCODE }
}
exit $taskExit
```

```bat
@echo off
nomozip-cli.exe test "D:\archives\first.zip" --use-vault
if errorlevel 1 exit /b %errorlevel%
nomozip-cli.exe extract "D:\archives\second.7z" --output "D:\result" --use-vault
exit /b %errorlevel%
```

复用与依据：[Microsoft Main/退出值](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/program-structure/main-command-line)、[PeaZip 命令行](https://peazip.github.io/peazip-command-line.html)，退出码参考随项目官方 SDK 的 `CPP/7zip/UI/Common/ExitCode.h`。伪终端验收依据 [ConPTY 文档](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session) 和 [Microsoft Terminal 标准句柄继承讨论](https://github.com/microsoft/terminal/discussions/15814)。仅借鉴接口行为，没有新增第三方命令行框架。
