// dist 驱动渐变与高光。推荐：Tools/地图/烘焙 UV 轮廓距离贴图并保存网格 + 轮廓模式 2；模式 5 顶点色易有插值条纹
Shader "Custom/SOC_HologramMap"
{
    Properties
    {
        [HDR] _InteriorColor ("内部颜色", Color) = (0.02, 0.07, 0.22, 1)
        _InteriorAlpha ("内部透明度", Range(0, 1)) = 1

        [HDR] _GradientColor ("渐变颜色", Color) = (0.06, 0.22, 0.48, 1)
        _GradientAlpha ("渐变透明度", Range(0, 1)) = 0.75
        _GradientBand ("渐变宽度", Range(0.002, 2)) = 0.14
        _GradientPower ("渐变曲线", Range(0.25, 8)) = 1.4
        _GradientEdgeCut ("渐离高光", Range(0, 1)) = 0.7

        [HDR] _EdgeHighlightColor ("边缘高光颜色", Color) = (0.35, 0.95, 1.0, 1)
        _EdgeHighlightAlpha ("边缘高光透明度", Range(0, 1)) = 0.95
        _EdgeHighlightBand ("高光带宽度", Range(0.001, 2)) = 0.035
        _EdgeHighlightPower ("高光锐利", Range(0.35, 12)) = 4.5

        _ContourMode ("轮廓模式 0主UV 1UV1 2贴图 3圆柱 4矩形 5顶点烘焙", Range(0, 5)) = 0
        _LocalContourScale ("物体模式距离缩放", Range(0.01, 10)) = 1
        _VertexContourAmp ("顶点轮廓距离倍率", Range(0.05, 2)) = 0.5

        _ContourTex ("轮廓距离场 R", 2D) = "white" {}
        _ContourSdfAmp ("轮廓贴图距离倍率", Range(0.02, 2)) = 0.45
        _ContourTexChannel ("贴图通道 0R 1G", Range(0, 1)) = 0
        _UV1DistanceInvert ("UV1 距离取反", Range(0, 1)) = 0
        _ContourTexInvert ("轮廓贴图距离取反", Range(0, 1)) = 0
        _UVRectAssist ("混合主UV矩形辅助", Range(0, 1)) = 0

        _CylCenterXZ ("圆柱中心 XZ", Vector) = (0, 0, 0, 0)
        _CylRadius ("圆柱半径", Range(0.001, 100)) = 0.5

        _BoxMinOS ("矩形最小 XYZ 物体空间", Vector) = (-0.5, -0.5, -0.5, 0)
        _BoxMaxOS ("矩形最大 XYZ 物体空间", Vector) = (0.5, 0.5, 0.5, 0)

        _GeomAmp ("几何法线增益", Range(1, 4000)) = 900
        _GeoEdgePower ("几何高光幂", Range(0.5, 12)) = 3
        _GeoEdgeBoost ("几何高光混入", Range(0, 1)) = 0.35
        _WorldUp ("顶面向上 XYZ", Vector) = (0, 1, 0, 0)
        _TopFaceCos ("顶面判定 cos", Range(0.7, 0.9999)) = 0.92

        _FresnelPower ("Fresnel 幂", Range(0.5, 10)) = 3.5
        _FresnelIntensity ("Fresnel 强度", Range(0, 3)) = 0.45
        _FresnelToEdge ("Fresnel 并入高光", Range(0, 1)) = 0.55

        _MaskTex ("遮罩 R", 2D) = "white" {}
        _MaskStrength ("遮罩增强", Range(0, 2)) = 0.35

        _DetailTex ("细节贴图", 2D) = "white" {}
        _DetailAmount ("细节影响", Range(0, 0.5)) = 0.12
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 viewDir : TEXCOORD3;
                float3 objPos : TEXCOORD4;
                float4 vc : COLOR;
                UNITY_FOG_COORDS(6)
            };

            float4 _InteriorColor;
            float _InteriorAlpha;
            float4 _GradientColor;
            float _GradientAlpha;
            float _GradientBand;
            float _GradientPower;
            float _GradientEdgeCut;
            float4 _EdgeHighlightColor;
            float _EdgeHighlightAlpha;
            float _EdgeHighlightBand;
            float _EdgeHighlightPower;
            float _ContourMode;
            float _LocalContourScale;
            float _VertexContourAmp;
            sampler2D _ContourTex;
            float _ContourSdfAmp;
            float4 _ContourTex_ST;
            float _ContourTexChannel;
            float _UV1DistanceInvert;
            float _ContourTexInvert;
            float _UVRectAssist;
            float4 _CylCenterXZ;
            float _CylRadius;
            float4 _BoxMinOS;
            float4 _BoxMaxOS;
            float _GeomAmp;
            float _GeoEdgePower;
            float _GeoEdgeBoost;
            float4 _WorldUp;
            float _TopFaceCos;
            float _FresnelPower;
            float _FresnelIntensity;
            float _FresnelToEdge;
            sampler2D _MaskTex;
            float4 _MaskTex_ST;
            float _MaskStrength;
            sampler2D _DetailTex;
            float4 _DetailTex_ST;
            float _DetailAmount;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.uv1 = v.uv1;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float4 wp = mul(unity_ObjectToWorld, v.vertex);
                o.viewDir = normalize(_WorldSpaceCameraPos - wp.xyz);
                o.objPos = v.vertex.xyz;
                o.vc = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float contourDistance(v2f i)
            {
                float2 u = saturate(i.uv);
                float uvRectDist = min(min(u.x, 1.0 - u.x), min(u.y, 1.0 - u.y));

                float mode = _ContourMode;
                float d;
                float lpX = i.objPos.x;
                float lpZ = i.objPos.z;

                if (mode < 0.5)
                    d = uvRectDist;
                else if (mode < 1.5)
                {
                    float v = saturate(i.uv1.x);
                    if (_UV1DistanceInvert > 0.5)
                        v = 1.0 - v;
                    d = v;
                }
                else if (mode < 2.5)
                {
                    float4 tc = tex2D(_ContourTex, TRANSFORM_TEX(i.uv, _ContourTex));
                    float v = _ContourTexChannel > 0.5 ? tc.g : tc.r;
                    if (_ContourTexInvert > 0.5)
                        v = 1.0 - v;
                    d = saturate(v) * _ContourSdfAmp * _LocalContourScale;
                }
                else if (mode < 3.5)
                {
                    float2 c = float2(lpX, lpZ) - _CylCenterXZ.xy;
                    float r = length(c);
                    d = max(0.0, _CylRadius - r) * _LocalContourScale;
                }
                else if (mode < 4.5)
                {
                    float dx = min(lpX - _BoxMinOS.x, _BoxMaxOS.x - lpX);
                    float dz = min(lpZ - _BoxMinOS.z, _BoxMaxOS.z - lpZ);
                    d = min(dx, dz) * _LocalContourScale;
                }
                else
                {
                    float useVert = step(i.vc.b, 0.06);
                    float dVert = saturate(i.vc.r) * _VertexContourAmp * _LocalContourScale;
                    d = lerp(uvRectDist, dVert, useVert);
                }

                float assist = saturate(_UVRectAssist);
                d = lerp(d, max(d, uvRectDist), assist);
                return max(d, 0.0);
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldNormal);
                float3 up = normalize(_WorldUp.xyz);
                // 仅朝上的顶面参与轮廓距离（与烘焙器一致，不用 abs 避免底面/侧壁误亮）
                float topFace = step(_TopFaceCos, dot(N, up));

                float dist = contourDistance(i);
                // 非顶面：推到「内部」，避免模式 0/5 未烘焙或 UV 辅助在侧壁出现矩形高光
                dist = lerp(1.0, dist, topFace);

                float rawGrad = pow(saturate(1.0 - dist / max(_GradientBand, 1e-4)), _GradientPower);
                float rawEdge = pow(saturate(1.0 - dist / max(_EdgeHighlightBand, 1e-4)), _EdgeHighlightPower);

                float3 fwN = float3(fwidth(N.x), fwidth(N.y), fwidth(N.z));
                float geo = length(fwN) * _GeomAmp * topFace;
                float geoEdge = pow(saturate(geo), _GeoEdgePower) * saturate(_GeoEdgeBoost);
                // 模式 5 顶点烘焙时关闭几何法线高光，薄挤出体上否则会整面过亮
                float vertBaked = (_ContourMode >= 4.5) ? step(i.vc.b, 0.06) : 0.0;
                rawEdge = saturate(max(rawEdge, geoEdge * (1.0 - vertBaked)));

                float cut = saturate(_GradientEdgeCut);
                float wGrad = saturate(rawGrad - rawEdge * cut);
                float wEdge = rawEdge;

                float3 V = normalize(i.viewDir);
                float ndv = saturate(dot(N, V));
                float fres = pow(1.0 - ndv, _FresnelPower) * _FresnelIntensity;
                wEdge = saturate(max(wEdge, fres * saturate(_FresnelToEdge)));

                float mask = tex2D(_MaskTex, TRANSFORM_TEX(i.uv, _MaskTex)).r;
                float mk = 1.0 + mask * _MaskStrength;
                wGrad *= mk;
                wEdge *= mk;

                wGrad = saturate(wGrad);
                wEdge = saturate(wEdge);

                float wInt = saturate(1.0 - max(rawGrad, rawEdge));

                float3 detail = tex2D(_DetailTex, TRANSFORM_TEX(i.uv, _DetailTex)).rgb;
                float3 interiorRgb = _InteriorColor.rgb * lerp(1.0, detail, _DetailAmount);

                half3 C = (half3)interiorRgb * (half)_InteriorAlpha * (half)wInt
                    + _GradientColor.rgb * (half)_GradientAlpha * (half)wGrad
                    + _EdgeHighlightColor.rgb * (half)_EdgeHighlightAlpha * (half)wEdge;

                half A = (half)_InteriorAlpha * (half)wInt
                    + (half)_GradientAlpha * (half)wGrad
                    + (half)_EdgeHighlightAlpha * (half)wEdge;
                A = saturate(A);

                half3 outRgb = A > 1e-3 ? C / A : (half3)0;
                half4 col = half4(outRgb, A);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
