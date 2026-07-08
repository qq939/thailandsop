# ORT CUDA 故障排查指南

本项目使用 ONNX Runtime + CUDA 进行推理，最常见的问题是 CUDA/cuDNN 运行时 DLL 缺失或版本不匹配。

## 快速检查清单
1) **ORT GPU Provider DLL 是否存在**
   - `onnxruntime_providers_cuda.dll`
   - `onnxruntime_providers_shared.dll`

2) **CUDA 运行时是否可用**
   - `cudart*.dll`、`cublas*.dll`、`cufft*.dll` 是否存在

3) **cuDNN 是否可用**
   - `cudnn*.dll` 是否存在

4) **DLL 搜索路径是否正确**
   - `CUDA_PATH\bin` 与 cuDNN bin 是否在 PATH 或已通过 `AddDllDirectory` 添加

5) **版本匹配**
   - ONNX Runtime GPU 版本需匹配 CUDA 主版本
   - cuDNN 主版本需匹配对应 CUDA 主版本

若上述任一项缺失，`LoadLibrary` 通常会返回 126 错误。

---

## 应用内置日志
程序启动会在输出目录生成以下日志：
- `native_preload.log`：显示哪些 DLL 缺失
- `cuda_provider_load.log`：显示 CUDA provider 是否加载成功

如果 `cuda_provider_load.log` 出现 `LoadLibrary failed (126)`，说明依赖 DLL 缺失或版本不匹配。

---

## 脚本化排查
在仓库根目录执行：

```powershell
# Release 构建
.\Tools\TroubleshootOrtCuda.ps1 -Config Release

# Debug 构建
.\Tools\TroubleshootOrtCuda.ps1 -Config Debug
```

脚本输出日志：
```
Logs\TroubleshootOrtCuda.log
```

---

## 如何解读日志
- **ORT provider DLL 缺失**：输出目录未正确复制 GPU Provider
- **CUDA DLL 缺失**：CUDA Runtime 未安装或版本不匹配
- **cuDNN DLL 缺失**：cuDNN 未安装或 bin 路径未生效

---

## 常见修复方法
- 安装与 ONNX Runtime GPU 版本匹配的 CUDA
- 安装与 CUDA 主版本匹配的 cuDNN
- 确认 PATH 指向正确的 CUDA 与 cuDNN bin
- 重新编译确保 `runtimes\win-x64\native` 中有 provider DLL

---

## 本项目说明
- 程序启动会自动将 CUDA/cuDNN bin 加入 DLL 搜索路径
- 多 CUDA 版本共存时，以 `CUDA_PATH` 为优先
- 若自动回退 DirectML/CPU，请先检查日志确认 CUDA provider 是否成功加载
