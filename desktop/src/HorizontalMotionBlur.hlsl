// Normalized 21-tap Gaussian kernel, horizontal only. Premultiplied alpha is preserved.
sampler2D inputTexture : register(s0);
float radius : register(c0);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 step = float2(radius / 10.0, 0);
    float4 color = tex2D(inputTexture, uv) * 0.1005897958;
    color += (tex2D(inputTexture, uv - step * 1) + tex2D(inputTexture, uv + step * 1)) * 0.0974949731;
    color += (tex2D(inputTexture, uv - step * 2) + tex2D(inputTexture, uv + step * 2)) * 0.0887701832;
    color += (tex2D(inputTexture, uv - step * 3) + tex2D(inputTexture, uv + step * 3)) * 0.0759291614;
    color += (tex2D(inputTexture, uv - step * 4) + tex2D(inputTexture, uv + step * 4)) * 0.0610107952;
    color += (tex2D(inputTexture, uv - step * 5) + tex2D(inputTexture, uv + step * 5)) * 0.0460533644;
    color += (tex2D(inputTexture, uv - step * 6) + tex2D(inputTexture, uv + step * 6)) * 0.0326567254;
    color += (tex2D(inputTexture, uv - step * 7) + tex2D(inputTexture, uv + step * 7)) * 0.0217540690;
    color += (tex2D(inputTexture, uv - step * 8) + tex2D(inputTexture, uv + step * 8)) * 0.0136133485;
    color += (tex2D(inputTexture, uv - step * 9) + tex2D(inputTexture, uv + step * 9)) * 0.0080028747;
    color += (tex2D(inputTexture, uv - step * 10) + tex2D(inputTexture, uv + step * 10)) * 0.0044196072;
    return color;
}
