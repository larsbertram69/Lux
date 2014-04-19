using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;


public enum LuxLightingModels 
{
	BlinnPhong = 0,
	CookTorrence = 1
}

[ExecuteInEditMode]
[AddComponentMenu("Lux/Lux Setup")]
public class SetupLux : MonoBehaviour {
	
	public float Lux_HDR_Scale = 6.0f;
	public bool isLinear;
	
	public LuxLightingModels LuxLighting;
	
	// IBL
	public float Lux_IBL_DiffuseExposure = 1.0f;
	private float DiffuseExposure;
	public float Lux_IBL_SpecularExposure = 1.0f;
	private float SpecularExposure;
	public Cubemap diffuseCube = null;
	public bool diffuseIsHDR;
	public Cubemap specularCube = null;
	public bool specularIsHDR;
	private Cubemap PlaceHolderCube = null;
	
	// not needed as we use faked fresnel in deferred
	// public GameObject MainLightReference = null;
	
	private float linearFactorDiffuse;
	private float linearFactorSpecular;
	
	// Use this for initialization
	void Start () {
		UpdateLuxIBLSettings();
	}
	
	// Update is called once per frame
	void Update () {
		#if UNITY_EDITOR
		if(!Application.isPlaying) {
			UpdateLuxIBLSettings();
		}
		#endif
		//if (MainLightReference != null) {
		//	Shader.SetGlobalVector("Lux_MainLightDir", MainLightReference.transform.forward );
		//}
	}
	
	void UpdateLuxIBLSettings () {
		#if UNITY_EDITOR
		// LINEAR
		if (PlayerSettings.colorSpace == ColorSpace.Linear) {
			isLinear = true;
		}
		// GAMMA
		else {
			isLinear = false;
		}
		#endif
		if (LuxLighting == LuxLightingModels.CookTorrence) {	
			Shader.EnableKeyword("LUX_LIGHTING_CT");
			Shader.DisableKeyword("LUX_LIGHTING_BP");
		}
		else {
			Shader.DisableKeyword("LUX_LIGHTING_CT");
			Shader.EnableKeyword("LUX_LIGHTING_BP");
		}
		// LINEAR
		if (isLinear) {
			Shader.DisableKeyword("LUX_GAMMA");
			Shader.EnableKeyword("LUX_LINEAR");
			
			// exposure is already in linear space
			// Lux_HDR_Scale is in srgb so we convert it to linear space
			DiffuseExposure = Lux_IBL_DiffuseExposure;
			if (diffuseIsHDR) {
				DiffuseExposure *= Mathf.Pow(Lux_HDR_Scale,2.2333333f);
			}
			SpecularExposure = Lux_IBL_SpecularExposure;
			if (specularIsHDR) {
				SpecularExposure *= Mathf.Pow(Lux_HDR_Scale,2.2333333f);
			}
			
		}
		// GAMMA
		else {
			Shader.EnableKeyword("LUX_GAMMA");
			Shader.DisableKeyword("LUX_LINEAR");
			
			// exposure is in linear space so we convert it to srgb
			// Lux_HDR_Scale is already in srgb
			DiffuseExposure = Mathf.Pow(Lux_IBL_DiffuseExposure, 1.0f / 2.2333333f);
			
			if (diffuseIsHDR) {
				DiffuseExposure *= Lux_HDR_Scale;
			}
			SpecularExposure = Mathf.Pow(Lux_IBL_SpecularExposure, 1.0f / 2.2333333f);
			if (specularIsHDR) {
				SpecularExposure *= Lux_HDR_Scale;
			}
			
		}
		/// IBL
		Shader.SetGlobalVector("ExposureIBL", new Vector4(DiffuseExposure, SpecularExposure, 1.0f, 1.0f) );
		if(diffuseCube) {
			Shader.SetGlobalTexture("_DiffCubeIBL", diffuseCube);
		}
		if(specularCube) {
			Shader.SetGlobalTexture("_SpecCubeIBL", specularCube);
		}
		else {
			createPlaceHolderCube ();
			Shader.SetGlobalTexture("_SpecCubeIBL", PlaceHolderCube);
			#if UNITY_EDITOR
				DestroyImmediate(PlaceHolderCube, true);
			#endif
		}

	}
	
	
	void createPlaceHolderCube () {
		if( PlaceHolderCube == null ) {
			PlaceHolderCube = new Cubemap(16,TextureFormat.ARGB32,true);
			for(int face = 0; face < 6; face++) {
				for(int x = 0; x < 16; x++) {
					for(int y = 0; y < 16; y++) {
						PlaceHolderCube.SetPixel((CubemapFace)face, x, y, Color.black);
					}
				}
			}
			PlaceHolderCube.Apply(true);
		}
	}
}

