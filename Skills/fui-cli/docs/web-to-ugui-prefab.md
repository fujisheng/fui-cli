# Web 原型生成 UGUI/FUI prefab

## 固定入口

通用 Web visual-ui 提取脚本位于：

```text
Packages/com.fujisheng.fui.cli/Tools/WebToUgui/extract-visual-ui.mjs
```

正式 prefab 生成工具：`ui.web_to_ugui_prefab`。

C# 实现：`Packages/com.fujisheng.fui.cli/Editor/WebVisualUiPrefabTool.cs`。

## 硬约束

- 设计分辨率必须来自用户或项目约定；未知时必须先问。
- 没有参考图时，基于功能需求和项目风格生成原创 Web 原型。
- Web、截图、`visual-ui.json` 必须放在项目根目录 `Temp/WebToUgui/<ViewName>/`。
- `visual-ui.json` 只能由固定提取脚本从 Web DOM 生成，禁止手写、拼接或人工编辑。
- prefab 输出路径必须按当前项目资源结构决定；不能固定照搬 demo 路径。

## Web 原型分层

### 视觉参考层：不提取

不要加 `data-ui-id`：

- 背景图、天空、地面、山体、建筑、云、光效
- 角色立绘、怪物、武器、坐骑、场景物件
- 纯装饰边框、纹理、粒子、阴影、高光

### 可提取 UI 层：生成 prefab

需要加 `data-ui-id` 和 `data-ui-type`：

- 按钮、文本、面板、进度条、图标占位
- Input、Toggle 等可交互控件
- 需要 FUI 绑定或运行态检查的元素

## 固定提取命令

```powershell
node Packages/com.fujisheng.fui.cli/Tools/WebToUgui/extract-visual-ui.mjs `
  --input Temp/WebToUgui/MobaHomeView/MobaHomeView.html `
  --view MobaHomeView `
  --width 1920 `
  --height 1080
```

默认输出：

```text
Temp/WebToUgui/MobaHomeView/MobaHomeView.visual-ui.json
Temp/WebToUgui/MobaHomeView/MobaHomeView.web.png
```

## prefab dry-run

```powershell
'{"args":{"json_file":"Temp/WebToUgui/MobaHomeView/MobaHomeView.visual-ui.json","prefab_path":"Assets/Resources/UI/Prefabs/MobaHomeView.prefab","dry_run":true}}' |
  Library/UnityCliBridge/unitycli.exe invoke --tool ui.web_to_ugui_prefab --stdin
```

## prefab 生成

```powershell
'{"args":{"json_file":"Temp/WebToUgui/MobaHomeView/MobaHomeView.visual-ui.json","prefab_path":"Assets/Resources/UI/Prefabs/MobaHomeView.prefab","dry_run":false}}' |
  Library/UnityCliBridge/unitycli.exe invoke --tool ui.web_to_ugui_prefab --stdin
```

## 检查清单

- [ ] 分辨率来源明确
- [ ] Web 原型区分视觉参考层和 UI 层
- [ ] `visual-ui.json` 由固定提取脚本生成
- [ ] JSON 中不包含背景、角色、装饰节点
- [ ] dry-run `issues` 为空
- [ ] prefab 路径符合项目资源结构
- [ ] Unity Console 无 error
