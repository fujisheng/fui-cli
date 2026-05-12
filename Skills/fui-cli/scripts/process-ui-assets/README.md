# process-ui-assets

通用 UI 位图后处理工具，配合 `asset-manifest.json` 使用。

## 边界

- 只处理 `imagegen` 已生成的独立 PNG。
- 可以做透明处理、裁透明边、缩放、复制和报告。
- 不创建最终美术。
- 不把 `design-master.png` 当整屏 UI。
- 不直接修改 Unity prefab，prefab 仍由 `ui.web_to_ugui_prefab` 生成。

## 用法

```powershell
node Packages\fui-cli\Skills\fui-cli\scripts\process-ui-assets\process-ui-assets.mjs `
  --manifest Temp\WebToUgui\LoginView\asset-manifest.json
```

只验证不写文件：

```powershell
node Packages\fui-cli\Skills\fui-cli\scripts\process-ui-assets\process-ui-assets.mjs `
  --manifest Temp\WebToUgui\LoginView\asset-manifest.json `
  --dry-run
```

处理单个资源：

```powershell
node Packages\fui-cli\Skills\fui-cli\scripts\process-ui-assets\process-ui-assets.mjs `
  --manifest Temp\WebToUgui\LoginView\asset-manifest.json `
  --asset WechatLoginButton
```

## manifest 字段

兼容当前 `asset-manifest.json`：

```json
{
  "assets": [
    {
      "path": "Assets/Resources/UI/Login/Elements/login_wechat_button_772x124.png",
      "tempPath": "Temp/WebToUgui/LoginView/assets/login_wechat_button_772x124.png",
      "usedBy": ["WechatLoginButton"],
      "size": { "width": 772, "height": 124 },
      "transparent": true,
      "fit": "stretch",
      "alphaMode": "trim"
    }
  ]
}
```

输入图查找顺序：

1. `source`
2. `rawPath`
3. `input`
4. `assets_raw/<输出文件名>`
5. `tempPath`
6. `path`

输出顺序：

1. 写入 `tempPath`
2. 如果 `path` 不同，再复制到 `path`

## Python 单图工具

```powershell
python Packages\fui-cli\Skills\fui-cli\scripts\process-ui-assets\postprocess_asset.py `
  --source Temp\WebToUgui\LoginView\assets_raw\button.png `
  --output Temp\WebToUgui\LoginView\assets\button.png `
  --width 772 `
  --height 124 `
  --fit stretch `
  --alpha-mode trim
```
