namespace LocalRAGChat.Server.Services;

public static class CosineSimilarityHelper
{
    public static double Calculate(float[] v1, float[] v2)
    {
        if (v1.Length != v2.Length)
            throw new ArgumentException("Vectors must be of the same length");

        double dotProduct = 0.0;
        double norm1 = 0.0;
        double norm2 = 0.0;

        for (int i = 0; i < v1.Length; i++)
        {
            dotProduct += v1[i] * v2[i];
            norm1 += v1[i] * v1[i];
            norm2 += v2[i] * v2[i];
        }

        if (norm1 == 0 || norm2 == 0)
            return 0.0;

        return dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }
}