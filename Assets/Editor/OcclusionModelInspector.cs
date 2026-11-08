using System;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

public static class OcclusionModelInspector
{
    private const string ModelPath = "Assets/Resources/AI/Models/selfie_segmenter.onnx";

    [MenuItem("Tools/Digital Human/Inspect Occlusion Model")]
    public static void Inspect()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
        if (!modelAsset)
        {
            throw new InvalidOperationException($"Could not import occlusion model at {ModelPath}.");
        }

        var model = ModelLoader.Load(modelAsset);
        Debug.Log($"Occlusion model: {model}");

        if (model.inputs.Count != 1 || !model.inputs[0].shape.IsStatic())
        {
            throw new InvalidOperationException("The M3 prototype requires one static model input.");
        }

        var input = model.inputs[0];
        var inputShape = input.shape.ToTensorShape();
        using var inputTensor = new Tensor<float>(inputShape);
        using var worker = new Worker(model, BackendType.CPU);
        worker.Schedule(inputTensor);

        for (var index = 0; index < model.outputs.Count; index++)
        {
            var output = worker.PeekOutput(index);
            Debug.Log(
                $"Occlusion output[{index}] name={model.outputs[index].name} " +
                $"type={output.dataType} shape={output.shape}");
        }
    }
}
