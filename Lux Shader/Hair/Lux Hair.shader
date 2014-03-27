//  //////////////////////////////////////
//  Based on the work of farfarer: http://www.farfarer.com/blog/2011/07/08/unity-shaders-tf2-anisotropic/
//  ATI: http://amd-dev.wpengine.netdna-cdn.com/wordpress/media/2012/10/Scheuermann_HairRendering.pdf


Shader "Lux Hair" {
      Properties {
          _Color ("Main Color", Color) = (1,1,1,1)
          _MainTex ("Diffuse (RGB) Alpha (A)", 2D) = "white" {}
          _BumpMap ("Normalmap", 2D) = "bump" {}

          _SpecularColor1 ("Specular Color1", Color) = (0.15,0.15,0.15,1)
          _PrimaryShift ("Primary Spec Shift", Range(0,1)) = 0.2
          _Roughness1 ("Roughness1", Range(0,1)) = 0.5

          _SpecularColor2 ("Specular Color2", Color) = (0.15,0.15,0.15,1)
          _SecondaryShift ("Secondary Spec Shift", Range(0,1)) = 0.5
          _Roughness2 ("Roughness2", Range(0,1)) = 0.5

          _RimStrength("Rim Light Strength", Range(0,1)) = 0.5

          _Cutoff ("Alpha Cut Off Threshold", Range(0,1)) = 0.5

          _DiffCubeIBL ("Custom Diffuse Cube", Cube) = "black" {}
          _SpecCubeIBL ("Custom Specular Cube", Cube) = "black" {}
      }

      SubShader{
          Tags {"Queue"="AlphaTest" "IgnoreProjector"="True" "RenderType"="LuxTransparentCutout"}

          CGPROGRAM
          #pragma surface surf LuxHair vertex:vert fullforwardshadows noambient nodirlightmap nolightmap alphatest:_Cutoff
          #pragma target 3.0
          #pragma glsl

          #pragma multi_compile LUX_LINEAR LUX_GAMMA
          #pragma multi_compile DIFFCUBE_ON DIFFCUBE_OFF
          #pragma multi_compile SPECCUBE_ON SPECCUBE_OFF

      //  #define LUX_LINEAR
      //  #define DIFFCUBE_ON
      //  #define SPECCUBE_ON
      //  Ambient Occlusion is stored in vertex color red
          #define LUX_AO_OFF

          fixed4 _Color;
          fixed4 _SpecularColor1;
          fixed4 _SpecularColor2;
          float _PrimaryShift;
          float _SecondaryShift;
          float _Roughness1; 
          float _Roughness2;
          float _RimStrength;

          sampler2D _MainTex;
          sampler2D _SpecularTex;
          sampler2D _BumpMap;

          #ifdef DIFFCUBE_ON
            samplerCUBE _DiffCubeIBL;
          #endif
          #ifdef SPECCUBE_ON
            samplerCUBE _SpecCubeIBL;
          #endif
          
          // Is set by script
          float4 ExposureIBL;

          // As we do not include Lux direct lighting functions we have to define these constants here
          #define OneOnLN2_x6 8.656170
          #define Pi 3.14159265358979323846
              
          struct SurfaceOutputAniso {
              fixed3 Albedo;
              fixed3 Normal;
              fixed4 AnisoDir;
              fixed3 Emission;
              half Specular;
              // Needed by LuxHair Lighting
              half2 Specular12;
              // Needed by Lux Ambient lighting
              fixed3 SpecularColor;
              fixed Alpha;
          };

          struct Input
          {
              float2 uv_MainTex;
              float2 uv_BumpMap;
              float3 tangent;
              fixed4 color : COLOR; // R stores Ambient Occlusion
              float3 viewDir;
              float3 worldNormal;
              float3 worldRefl;
              INTERNAL_DATA
          };

          void vert (inout appdata_full v, out Input o)
          {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.tangent = v.tangent.xyz;
          }

          void surf (Input IN, inout SurfaceOutputAniso o)
          {
             fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex);
              o.Albedo = albedo.rgb * _Color.rgb;
              o.Alpha = albedo.a;
              o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
              // Lux Ambient Lighting functions need o.SpecularColor(rgb) and o.Specular(half)
              // So we have to make it a bit more complicated here  
              o.SpecularColor = _SpecularColor1.rgb;
              o.Specular = _Roughness1*_Roughness1;
              o.Specular12 = half2(_Roughness1, _Roughness2);
              o.AnisoDir = fixed4(IN.tangent, 1.0);

              #include "../LuxCore/LuxLightingAmbient.cginc"

              // Add ambient occlusion stored in vertex color red
              o.Emission *= IN.color.r;
          }

          // //////////////

          inline fixed4 LightingLuxHair (SurfaceOutputAniso s, fixed3 lightDir, fixed3 viewDir, fixed atten)
          {
            // normalizing lightDir makes fresnel smoother
            lightDir = normalize(lightDir);
            fixed3 h = normalize(normalize(lightDir) + normalize(viewDir));
            float dotNL = max(0,dot(s.Normal, lightDir));
            
            // Shift Tangents
            fixed2 dotHA;
            // dotHA.x = dot(normalize(s.Normal * _PrimaryShift * s.AnisoJitter  + s.AnisoDir.rgb ), h) ;
            // dotHA.y = dot(normalize(s.Normal * _SecondaryShift * s.AnisoJitter + s.AnisoDir.rgb ), h) ;
            dotHA.x = dot(normalize(s.Normal * _PrimaryShift + s.AnisoDir.rgb ), h) ;
            dotHA.y = dot(normalize(s.Normal * _SecondaryShift + s.AnisoDir.rgb ), h) ;

            float2 aniso;
            aniso =  max(fixed2(0,0), sin( dotHA * Pi ));

            // Bring specPower into a range of 0.25 – 2048
            float2 specPower = exp2(10 * s.Specular12 + 1) - 1.75;
            // Calculate primary and secondoary specular values 
            float3 spec = specPower.x * pow(aniso.x, specPower.x) * _SpecularColor1;
            spec += specPower.y * pow(aniso.y, specPower.y) * _SpecularColor2;
            // Normalize
            spec *= 0.125;


            // Rim
            fixed RimPower = saturate (1.0 - dot(s.Normal, viewDir));
            fixed Rim = _RimStrength * RimPower*RimPower;
            fixed4 c;
             
            // Diffuse Lighting: Lerp shifts the shadow boundary for a softer look
            float3 diffuse = saturate (lerp (0.25, 1.0, dotNL));
            // Combine and apply late dotNL
            c.rgb = ((s.Albedo + Rim)* diffuse + spec * dotNL ) * _LightColor0.rgb  * (atten * 2);

            c.a = s.Alpha;
            return c;
          }

          ENDCG
      }
      FallBack "Transparent/Cutout/VertexLit"
      CustomEditor "LuxMaterialInspector"
}