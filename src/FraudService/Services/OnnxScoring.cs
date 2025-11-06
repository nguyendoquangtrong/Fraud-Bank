using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class OnnxScoring : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string[] _order; // f0..fN (khóa logic), KHÔNG phải tên gốc

    public OnnxScoring(string modelPath, string metadataPath)
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException(modelPath);
        _session = new InferenceSession(modelPath);

        // Đọc metadata linh hoạt
        var (inputName, order, featureMap, raw) = ReadMetaSafe(metadataPath, _session);

        _inputName = inputName;
        _order = order;

        // Log nhẹ để bạn thấy nó đã pick cái gì
        Console.WriteLine($"[ONNX] input_name = {_inputName}");
        Console.WriteLine($"[ONNX] order = [{string.Join(",", _order)}]");
        if (featureMap is not null)
            Console.WriteLine($"[ONNX] feature_mapping keys = [{string.Join(",", featureMap.Keys)}]");
        else
            Console.WriteLine($"[ONNX] feature_mapping = <null> (dùng default/or-order)");
    }

    public (double risk, string label) Predict(
        IDictionary<string,double> featuresByOriginalName,
        IDictionary<string,string> reverseMap) 
    {
        var vector = new float[_order.Length];
        for (int i = 0; i < _order.Length; i++)
        {
            var fx = _order[i];    
            if (!reverseMap.TryGetValue(fx, out var original))
                throw new KeyNotFoundException($"reverseMap thiếu khóa '{fx}'. Hãy kiểm tra metadata/LoadReverseMap.");
            vector[i] = (float)(featuresByOriginalName.TryGetValue(original, out var val) ? val : 0d);
        }

        var tensor = new DenseTensor<float>(vector, new[] { 1, vector.Length });

        var input = NamedOnnxValue.CreateFromTensor(_inputName, tensor);
        using var results = _session.Run(new[] { input });

        var labelTensor = results.First(x => x.Name.Contains("label", StringComparison.OrdinalIgnoreCase)).AsTensor<long>();
        var label = labelTensor[0]; // 0/1

        var probNode = results.FirstOrDefault(x => x.Name.Contains("probability", StringComparison.OrdinalIgnoreCase));
        double risk;
        if (probNode is not null)
        {
            // đa số model xuất tensor float (2 giá trị: [p0, p1])
            if (probNode.Value is Tensor<float> tf && tf.Length >= 2)
            {
                risk = tf.ToArray()[1];
            }
            else
            {
                // fallback nếu probability không phải tensor (vd: map)
                // => dùng nhãn
                risk = (label == 1) ? 1.0 : 0.0;
            }
        }
        else
        {
            risk = (label == 1) ? 1.0 : 0.0;
        }

        return (risk, label == 1 ? "BLOCK" : "ALLOW");
    }

    public void Dispose() => _session.Dispose();


    private static (string inputName, string[] order, Dictionary<string,string>? featureMap, JsonDocument? raw)
        ReadMetaSafe(string metadataPath, InferenceSession session)
    {
        Dictionary<string,string>? featureMap = null;
        string? inputNameFromMeta = null;
        string[]? inputOrderFromMeta = null;

        if (File.Exists(metadataPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var root = doc.RootElement;

            // input_name
            if (root.TryGetProperty("input_name", out var inProp) && inProp.ValueKind == JsonValueKind.String)
                inputNameFromMeta = inProp.GetString();

            // input_order
            if (root.TryGetProperty("input_order", out var orderProp) && orderProp.ValueKind == JsonValueKind.Array)
                inputOrderFromMeta = orderProp.EnumerateArray()
                                              .Where(e => e.ValueKind == JsonValueKind.String)
                                              .Select(e => e.GetString()!)
                                              .ToArray();

            // feature_mapping
            if (root.TryGetProperty("feature_mapping", out var fmapProp) && fmapProp.ValueKind == JsonValueKind.Object)
            {
                featureMap = new Dictionary<string,string>(StringComparer.Ordinal);
                foreach (var kv in fmapProp.EnumerateObject())
                    if (kv.Value.ValueKind == JsonValueKind.String)
                        featureMap[kv.Name] = kv.Value.GetString()!;
            }

            // Ưu tiên dùng input_name trong metadata nếu có
            var inputName = !string.IsNullOrWhiteSpace(inputNameFromMeta)
                ? inputNameFromMeta!
                : session.InputMetadata.Keys.First(); // fallback: input đầu tiên

            // Xác định order:
            string[] order;
            if (inputOrderFromMeta is { Length: > 0 })
            {
                order = inputOrderFromMeta!;
            }
            else if (featureMap is { Count: > 0 })
            {
                order = featureMap.Keys
                                  .OrderBy(k => ParseIndex(k)) // "f0"->0
                                  .ToArray();
            }
            else
            {
                // fallback cuối cùng: dùng default đã thống nhất
                order = DefaultFxOrder();
            }

            return (inputName, order, featureMap, doc);
        }
        else
        {
            // Không có metadata.json: lấy input đầu tiên + default order
            var inputName = session.InputMetadata.Keys.First();
            var order = DefaultFxOrder();
            return (inputName, order, null, null);
        }

        static int ParseIndex(string fx)
        {
            // "f12" -> 12; nếu không parse được thì đẩy cuối
            for (int i = 0; i < fx.Length; i++)
                if (char.IsDigit(fx[i]) && int.TryParse(fx.AsSpan(i), out var n)) return n;
            return int.MaxValue;
        }

        static string[] DefaultFxOrder() => new[]
        {
            "f0","f1","f2","f3","f4","f5","f6","f7","f8","f9","f10","f11"
        };
    }
}
