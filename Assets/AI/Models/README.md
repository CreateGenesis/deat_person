# Foreground segmentation model

## Selfie Segmenter

- File: `selfie_segmenter.onnx`
- Original model: <https://storage.googleapis.com/mediapipe-models/image_segmenter/selfie_segmenter/float16/latest/selfie_segmenter.tflite>
- ONNX conversion: <https://github.com/PINTO0309/PINTO_model_zoo/tree/main/109_Selfie_Segmentation>
- Conversion archive: <https://s3.ap-northeast-2.wasabisys.com/pinto-model-zoo/109_Selfie_Segmentation/resources.tar.gz>
- SHA-256: `DE212DABBC6266F0047711D1DFAE80900F7B596B9ED5F7665F3D1CF68C5443EE`
- Upstream project: <https://github.com/google-ai-edge/mediapipe>
- Upstream license: Apache License 2.0
- Intended use: real-time person foreground segmentation for AR occlusion

The model is used only to produce a foreground confidence mask. Runtime code keeps
the model reference configurable so a hand-specific or multiclass segmentation
model can replace it after target-device testing.

The official MediaPipe TFLite uses the custom `Convolution2DTransposeBias`
operator, which Unity AI Inference 2.6.1 cannot import. The converted ONNX model
uses the same 256 x 256 NHWC input/output contract without that custom operator.

The first M3 implementation assumes the phone user is closer than the virtual
character. A 2D foreground mask does not provide metric depth and therefore cannot
determine whether a detected person is physically behind the character.
