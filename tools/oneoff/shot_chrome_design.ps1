这是一次性脚本占位文件（本会话沙箱限制删除权限，未能移除）。

用途：用 Edge headless 把
`docs/UI设计基线/app-shell-chrome/design.html` 的六种状态渲染成 PNG，
输出到 `docs/UI设计基线/app-shell-chrome/evidence/`。

本会话实测：目录创建被沙箱拒绝（`mkdir` → Permission denied），脚本实际未产出任何图片。
需要时由有写权限的会话执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\oneoff\shot_chrome_design.ps1
```

执行前请先确认 `evidence/` 目录已存在。用完即删（AGENTS.md R166：一次性脚本放 tools\oneoff\，用完删）。
