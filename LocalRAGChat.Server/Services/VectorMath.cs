namespace LocalRAGChat.Server.Services;

public static class VectorMath
{
    public static double CosineSimilarity(float[] vec1, float[] vec2)
    {
        if (vec1 == null || vec2 == null || vec1.Length != vec2.Length || vec1.Length == 0)
        {
            return 0.0;
        }

        var dotProduct = 0.0;
        var norm1 = 0.0;
        var norm2 = 0.0;

        for (int i = 0; i < vec1.Length; i++)
        {
            dotProduct += vec1[i] * vec2[i];
            norm1 += vec1[i] * vec1[i];
            norm2 += vec2[i] * vec2[i];
        }

        if (norm1 == 0 || norm2 == 0) return 0.0;
        return dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }
}
    
