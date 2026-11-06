# source/ — 预编译的 DQQ 程序集

本目录放 DQQ、DQQ.Core、DQQ.Web.Component 编译好的 DLL 及**全部依赖**（含中文附属资源），runtime 只引用这里的文件（架构 D23）。这样在 GitHub 上编译时不需要 DQQ 源码，也不需要访问令牌。

- 更新方式：在 Windows 上运行 `tools/update-dqq.ps1`（默认从 `C:\code\dqq\DQQ` 编译），然后提交本目录。
- 版本记录：`dqq/VERSION.json`（DQQ 提交号、未提交文件数、编译时间、SDK 版本）。
- 不要手工修改本目录的文件。
