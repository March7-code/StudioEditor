# 恋活场景与角色的材质兼容

2026-10-14。兼容逻辑分为源数据读取、通用渲染上下文和 URP 桥接，避免针对某张场景卡改颜色或隐藏物件。

## 发色和角色材质

`Shader Forge/main_hair*` 的 `_ColorMask` 按原版 DXBC 中的顺序混合处理，使用 `CharacterColorMaskBlend.Sequential`，而非将三个通道归一化为权重。保留原始 UV 变换和渐变色。普通头发不默认混入顶点色；`cf_acs*` 按饰品处理。MaterialEditor 的最终覆盖仍在基础材质转换之后应用。

## 灯光和调色

- `KoikatsuSceneReader` 读取全部相机槽之后的角色灯、地图灯和 Filter 设置。
- `KoikatsuStudioLightRig` 从原版 Unity 5.6.2 的 `CharaStudio_Data/level1` 读取角色灯的初始旋转，再与场景卡保存的旋转组合。解析缓存使用 `studio-character-light-rotation-v2`。
- `ImportedSceneRenderContext` 保存源灯光、地图主灯、角色渲染器、灯光作用对象和 LUT。Studio 附加灯从预制体读取实际 culling mask，再执行 `AddObjectLight.Load` 的 Target 层切换；不能只凭列表中的“全体光”名称扩大到人物。原版 `p_stneo_Directionallight` 的遮罩为 2064，不包含 Chara（1024）。同时保留源 RenderMode 与阴影强度、偏移、近裁面，桥接到 URP Rendering Layers。
- 地图太阳保留 URP 主灯位置；角色灯按独立层参与附加灯计算，并保留完整强度。地图主灯的阴影层包含角色，使人物仍可向地图投影。禁用或卸载上下文时恢复太阳、阴影层和渲染层。
- 附加方向灯无法使用 URP 主方向灯的级联阴影。角色灯改为附加灯后，其方向阴影仍需要独立方案；实验中的局部遮蔽不能替代完整的角色灯阴影。
- 原版 LUT 默认启用，并明确覆盖 Tonemapping=None、postExposure=0，避免 URP 的 Neutral 曲线叠加压色。

原版调色的代码开关位于 `Rendering/URP/ImportedRenderConfiguration.cs`：

```csharp
ImportedRenderConfiguration.ApplySourceColorGrading = true; // 默认
```

Amplify Color 的 `BlendAmount` 表示从第一张 LUT 混合到第二张 LUT。原版 Studio 的 `level1` 已给 `LutBlendTexture` 绑定 filter 0（`Lut_Amount`，早晨），因此未保存 TwoLut 元数据的旧卡也必须加载这张第二 LUT；它不是未调色画面。保存有 `TwoLutPlugin` 扩展数据时读取 `slot` / `guid`。两张 LUT 在线性 ARGB32 数据目标中混合，URP ColorLookup contribution 为 1；只有第二张资源缺失时才回退为单 LUT。运行时切换开关会释放或建立 Volume 和混合缓存；`DisplayCameraScope` 负责恢复相机后处理状态和 Volume 层。

实验方案仍通过 `ExperimentalRenderConfiguration.Scheme` 切换，没有新增界面入口。地图预设通过 `SunLightInfo` 的源数据同步。

## 表情

眼睑开度遵循原版 `ChangeEyesOpenMax` 的上限 0.92、平方根插值和上下左右视线修正。`EyeLookCalc.SaveAngle` 中保存的骨骼旋转和瞳孔偏移保留。`FaceBlendShape` 的视线修正元数据缓存使用 `morph-v2`。

`cha_eyeset` 的 `EyeBase`、`EyeHitomi`、`EyeObjNo` 控制普通眼睛与特殊表情眼的分支。特殊眼材质保留贴图动画；虹膜表情使用贴图 Alpha 混合和视角 UV 偏移。泪水、汗水等尚未完整复现原版控制逻辑。

## 场景物件

- `Shader Forge/main_item_studio_add` 按原版的 RGB 三通道遮罩、八次幂通道排斥及逐分量 max 合成颜色，使用 `One / One` 加色。独立 `_alpha` 乘最终 RGB，纹理/颜色 Alpha 不参与。保留三套颜色、透明度和来源队列。
- `Unlit/Transparent` 保持透明效果。
- `Skybox/Cubemap` 保留 Cubemap、Tint、Exposure、Rotation；背景不作为普通投影物件。
- 衣服优先使用原版 `chara/mm_base.unity3d` 中的创建材质做 GPU 烘焙，保留 Mipmaps 和 Trilinear 采样；不可用时走兼容路径。原版 `create_topN` 使用 Alpha 混合，烘焙前须像 `CustomTextureCreate.RebuildTextureAndSetMaterial` 一样把目标清为透明，否则临时渲染目标的残留像素会填满领口等透明区域。

## 衣物遮罩

外衣的 `OverBraMask` / `OverInnerMask` 按穿着状态的红、绿通道裁剪内层衣物，并保留遮罩 UV 变换。转换后的 URP 材质优先从实际渲染使用的 `_BaseMap` 读取主贴图；隐藏的兼容槽位 `_MainTex` 可能为空或保留旧贴图，不能据此跳过遮罩烘焙。内层衣物继续保留穿着状态，仅遮罩完全透明时关闭对应渲染器。

## 验证

自动检查覆盖层隔离及恢复、地图主灯与阴影层、调色开关及相机恢复、顺序混色、虹膜 Alpha 混合、眼睑修正与场景尾部灯光读取。

`ClothingOcclusionTests` 覆盖兼容槽位为空、兼容槽位残留旧贴图、遮罩完全覆盖内层衣物，以及 Alpha 混合烘焙复用不透明目标时仍保留贴图透明孔洞的情况。实际场景验证保留完整外衣，并确认正常穿着状态下内层材质使用遮罩后的主贴图、领口显示皮肤。

两张实际场景卡 `2CHAN/1626540687330.png` 和 `2CHAN/1526794756848.png` 用于检查最新场景与街道场景的导入及渲染对比。隔离项目的截图和日志位于忽略目录 `Diagnostics`，不是构建资源。当前验证为 Unity Editor / DX12，尚未做独立 Player 验证。
