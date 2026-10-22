namespace ProMaxx2.QA.Application.Knowledge;

/// <summary>คำนวณเวกเตอร์ embedding — เก็บแบบ normalize แล้ว (ความยาว 1) ทำให้ cosine = dot product</summary>
public static class VectorMath
{
    public static float[] Normalize(IReadOnlyList<float> vector)
    {
        double sum = 0;
        for (var i = 0; i < vector.Count; i++) sum += vector[i] * (double)vector[i];
        var length = Math.Sqrt(sum);
        var result = new float[vector.Count];
        if (length == 0) return result;
        for (var i = 0; i < vector.Count; i++) result[i] = (float)(vector[i] / length);
        return result;
    }

    /// <summary>dot product ของเวกเตอร์ที่ normalize แล้ว (= cosine similarity) — ขนาดไม่เท่ากัน = 0</summary>
    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        // SIMD ด้วย System.Numerics.Vector<float> (ไม่ต้องเพิ่ม package) — หลักหมื่นช่วง × 1,024 มิติ ยังเสร็จในไม่กี่สิบ ms
        var width = System.Numerics.Vector<float>.Count;
        var acc = System.Numerics.Vector<float>.Zero;
        var i = 0;
        for (; i <= a.Length - width; i += width)
            acc += new System.Numerics.Vector<float>(a.Slice(i, width)) * new System.Numerics.Vector<float>(b.Slice(i, width));
        var sum = System.Numerics.Vector.Sum(acc);
        for (; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }
}

/// <summary>
/// รวมผลค้นด้วยคำ (Full-Text) กับผลค้นตามความหมาย (embedding) ด้วย Reciprocal Rank Fusion:
/// score = Σ 1 / (k + อันดับ) — ไม่ต้องปรับสเกลคะแนนของสองระบบให้เท่ากัน; ช่วงที่เจอทั้งสองแบบได้คะแนนสูงสุด
/// Rank ที่คืน = score × 100,000 (จำนวนเต็ม ใช้เรียงลำดับเท่านั้น)
/// </summary>
public static class HybridRanker
{
    public const int DefaultK = 60;

    public static IReadOnlyList<KnowledgeChunkHit> Fuse(IReadOnlyList<KnowledgeChunkHit> textHits, IReadOnlyList<KnowledgeChunkHit> vectorHits, int take, int k = DefaultK)
    {
        if (vectorHits.Count == 0) return textHits.Take(take).ToList();
        if (textHits.Count == 0) return vectorHits.Take(take).ToList();
        var scores = new Dictionary<int, double>();
        void Add(IReadOnlyList<KnowledgeChunkHit> hits)
        {
            // เรียงตาม Rank ของระบบนั้นก่อน (Full-Text: RANK สูง = ตรงกว่า; vector: similarity × 1000)
            var ordered = hits.OrderByDescending(x => x.Rank).ToList();
            for (var i = 0; i < ordered.Count; i++)
                scores[ordered[i].KnowledgeChunkId] = scores.GetValueOrDefault(ordered[i].KnowledgeChunkId) + 1.0 / (k + i + 1);
        }
        Add(textHits);
        Add(vectorHits);
        return scores.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(take)
            .Select(x => new KnowledgeChunkHit(x.Key, (int)Math.Round(x.Value * 100_000))).ToList();
    }
}
