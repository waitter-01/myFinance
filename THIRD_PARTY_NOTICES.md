# 第三方组件说明

独秀账本的本地截图识别功能使用以下第三方组件：

- `Tesseract` .NET wrapper 5.2.0，项目地址：<https://github.com/charlesw/tesseract>，采用 Apache License 2.0。
- Tesseract `tessdata_best` English model，项目地址：<https://github.com/tesseract-ocr/tessdata_best>，采用 Apache License 2.0。

数字专用模型仅用于账单金额与时间的小区域离线识别，截图不会因此上传到网络。
