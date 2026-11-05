# 版本管理约定

## 当前基线

- 默认分支：`main`。
- 当前应用版本：`src/DeltaMarketAssistant/DeltaMarketAssistant.csproj` 中的 `1.20.0`。
- 首个源码归档标签：`v1.20.0`，使用带说明的 annotated tag。
- Git 管理从当前导入开始，早期变化仅作为开发记录保存，不伪造旧提交或回填日期。
- 配置 `SchemaVersion = 11` 是配置格式版本，不随每个应用发布递增。

## 后续修改

1. 从 `main` 建立 `feature/<描述>` 或 `fix/<描述>` 分支。
2. 每次提交只表达一个可复核的变化，提交信息说明行为和原因。
3. 测试通过后合并回 `main`；建议通过 PR 查看差异。
4. 更新应用版本和 `CHANGELOG.md`，记录验证范围及已知限制。
5. 在最终提交建立 `vMAJOR.MINOR.PATCH` 标签，然后推送分支与标签。
6. 已发布标签保持不变；修复时建立新补丁版本，不强推覆盖旧发布。

当前小版本表示功能迭代，补丁版本表示兼容修复，不兼容行为或配置迁移需在发布说明中明确记录。

## 自动构建

`.github/workflows/build.yml` 在 main 推送、PR 和版本标签上运行 Windows 构建、21 项可移植回归及单文件发布。标签构建同时检查标签名与 csproj 版本一致。构建产物作为 Actions artifact 保留 14 天，不提交到 Git。

需要将 EXE 补充到已有 Release 时，手动运行 `release-executable.yml`，填写发布标签、成功构建的 run ID 和 artifact 名称。工作流核对构建来自本仓库的 `build.yml`、测试成功且源码提交与标签完全一致，再直接上传 EXE 和独立 SHA-256 校验文件。已上传且内容不同的文件不会被覆盖；中断上传的残留项允许重试。此流程不移动版本标签，也不打包本机配置。

## 源码打包与恢复

```powershell
git switch main
git log --oneline --decorate
git tag --list
git archive --format=zip --prefix=DeltaMarketAssistant-v1.20.0/ `
  --output=artifacts/DeltaMarketAssistant-v1.20.0-source.zip v1.20.0
```

打包前先建立 `artifacts` 目录。`git archive` 包含该标签跟踪的文件，不包含 `.git` 历史；完整版本历史用 `git clone` 获取。后续查阅旧版可使用新工作目录克隆或 `git worktree`，避免覆盖当前未提交的修改。

## 上传范围

源码仓库包含 `.sln`、项目代码、测试、内嵌模板、局部数字样本、资源生成脚本、构建脚本、开发说明、版本记录和 CI 配置。个人 `config.json`、日志、诊断图片、完整游戏截图、抓包文件、缓存、历史 EXE、`bin`、`obj` 与打包输出不进入 Git。需要长期保存安装包时使用 GitHub Releases 资产。
