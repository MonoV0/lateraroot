sampler2D backdrop : register(s0);
float4 mapping : register(c0); // crop UV
float4 shape : register(c1);   // width, height, corner radius, refraction DIP
float4 tint : register(c2);    // RGB, black-ink flag
float4 light : register(c3);   // light position, blur DIP, DPI

float3 sampleGlass(float2 uv, float2 stepSize)
{
    float3 c = tex2D(backdrop, uv).rgb * .20;
    c += tex2D(backdrop, uv + float2(stepSize.x, 0)).rgb * .12;
    c += tex2D(backdrop, uv - float2(stepSize.x, 0)).rgb * .12;
    c += tex2D(backdrop, uv + float2(0, stepSize.y)).rgb * .12;
    c += tex2D(backdrop, uv - float2(0, stepSize.y)).rgb * .12;
    c += tex2D(backdrop, uv + stepSize).rgb * .08;
    c += tex2D(backdrop, uv - stepSize).rgb * .08;
    c += tex2D(backdrop, uv + float2(stepSize.x, -stepSize.y)).rgb * .08;
    c += tex2D(backdrop, uv + float2(-stepSize.x, stepSize.y)).rgb * .08;
    return c;
}
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 size = max(shape.xy, 1);
    float radius = min(shape.z, min(size.x, size.y) * .5);
    float2 p = (uv - .5) * size;
    float2 q = abs(p) - (size * .5 - radius);
    float2 outside = max(q, 0);
    float distance = length(outside) + min(max(q.x, q.y), 0) - radius;
    float2 straightNormal = q.x > q.y ? float2(sign(p.x), 0) : float2(0, sign(p.y));
    float2 normal = length(outside) > .001 ? normalize(outside) * sign(p) : straightNormal;
    float bevel = saturate(1 + distance / min(15, min(size.x, size.y) * .3));
    float lens = bevel * bevel;
    float2 refracted = .5 + (uv - .5) / (1 + .018 * saturate(shape.w / 9)) + normal * shape.w * lens / size;
    float2 source = mapping.xy + refracted * mapping.zw;
    float2 blur = light.z * mapping.zw / size;
    float3 color = sampleGlass(source, blur);
    color = lerp(color, tint.rgb, tint.a > .5 ? .10 : .22);
    // Adapt light transmission while preserving readable black/white foregrounds.
    float3 energy = pow(max(color, .001), 2.2);
    float luma = dot(energy, float3(.2126, .7152, .0722));
    if (tint.a > .5) energy = lerp(energy, 1, saturate((.25 - luma) / max(.001, 1 - luma)));
    else energy *= min(1, .13 / max(.001, luma));
    color = pow(max(energy, .001), 1 / 2.2);
    float2 lightDirection = normalize(light.xy - .5 + float2(-.1, -.1));
    float shine = pow(saturate(dot(normal, lightDirection)), 3);
    float rim = saturate(1 - abs(distance + .6) / 1.0);
    color += rim * (.12 + shine * .42);
    color += lens * shine * .07;
    color -= rim * (1 - shine) * .055;
    float alpha = saturate(.5 - distance * max(light.w, 1));
    return float4(saturate(color) * alpha, alpha);
}
