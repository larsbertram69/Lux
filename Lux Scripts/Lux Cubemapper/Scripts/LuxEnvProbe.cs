//  ////////////////////////////////////
//  Modified version of CubemapMaker.js from BlackIce studio http://www.blackicegames.de/development/?site=dl&id=1
//  Converted to C# and make it compatible for Lux
//

using UnityEngine;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
using System.Collections.Generic;
using System.IO;
#endif

[ExecuteInEditMode]
[AddComponentMenu("Lux/Lux Environment Probe")]
public class LuxEnvProbe : MonoBehaviour {
    //public var
    public enum FaceSizes
    {
        _16px = 16,
        _32px = 32,
        _64px = 64,
        _128px = 128,
        _256px = 256,
        _512px = 512,
    };
    public enum CubeModes
    {
        Standard,
        Box
    }

    [HideInInspector]
    public Cubemap DIFFCube;
    [HideInInspector]
    public Cubemap SPECCube;
    [HideInInspector]
    public FaceSizes DiffSize = FaceSizes._16px;
    [HideInInspector]
    public FaceSizes SpecSize = FaceSizes._64px;
    [HideInInspector]
    public CameraClearFlags ClearFlags = CameraClearFlags.Skybox;
    [HideInInspector]
    public Color ClearColor = Color.black;
    [HideInInspector]
    public LayerMask CullingMask = -1;
    [HideInInspector]
    public float Near = 0.1f;
    [HideInInspector]
    public float Far = 1000f;
    [HideInInspector]
    public bool UseRTC = false;
    [HideInInspector]
    public bool SmoothEdges = false;
    [HideInInspector]
    public int SmoothEdgePixel = 3;
    [HideInInspector]
    public bool HDR = false;
    [HideInInspector]
    public bool Linear = false;
    
    // BoxProjection
    [HideInInspector]
    public CubeModes Mode = CubeModes.Standard;
    [HideInInspector]
    public Vector3 BoxSize = new Vector3(1f,1f,1f);
    [HideInInspector]
    public bool ShowAssignedMeshes;
    [HideInInspector]
    public List<GameObject> AssignedMeshes;

	// Use this for initialization
    [HideInInspector]
    public bool init = false;

    //Helper
    [HideInInspector]
    public string DiffPath;
    [HideInInspector]
    public string SpecPath;
    [HideInInspector]
    public string sceneName;
    [HideInInspector]
    public string cubeName;

    //private var
    bool mipmap;
    Camera CubeCamera;
    Cubemap cubemap;
    TextureFormat texFor;
    GameObject go;
    bool Done = false;
    int size;

    public void PreSetup()
    {
        if (EditorApplication.currentScene != "")
        {
            List<string> pathTemp = new List<string>(EditorApplication.currentScene.Split(char.Parse("/")));
            pathTemp[pathTemp.Count - 1] = pathTemp[pathTemp.Count - 1].Substring(0, pathTemp[pathTemp.Count - 1].Length - 6);
            sceneName = string.Join("/", pathTemp.ToArray()) + "/";
        }

        if(Mode == CubeModes.Standard) {
            cubeName = sceneName + SpecSize.ToString() + "@" + transform.position.ToString();
        }
        // When baking Box projected cubemaps one might pretty often change the position of the probe just a little bit...
        else {
            cubeName = sceneName + SpecSize.ToString() + "Box@" + GetInstanceID();
        }
        DiffPath = cubeName + "DIFF.cubemap";
        SpecPath = cubeName + "SPEC.cubemap";


    }
	void Start () {
        PreSetup();
        //Do directory check
        if (!Directory.Exists(sceneName))
        {
            //if it doesn't, create it
            Directory.CreateDirectory(sceneName);
        }

        if (!EditorApplication.isPlaying)
        {
            DestroyImmediate(cubemap);
            DestroyImmediate(go);
            DestroyImmediate(CubeCamera);
            RetriveCubemap();
        }
	
	}

    void OnDrawGizmos()
    {
        Gizmos.color = new Color (0.0f, 1.0f, 1.0f, 0.75f);
        if (Mode == CubeModes.Standard ) {
            Gizmos.DrawSphere(transform.position, 0.15f);
        }
        // Mode = Boxprojection
        else {
            // Draw center 
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.zero, new Vector3(0.35f,0.35f,0.35f));
            // Draw bounding box
            Gizmos.DrawWireCube (Vector3.zero, BoxSize);
            // reset matrix
            Gizmos.matrix = Matrix4x4.identity;   
        }
    }

    // Sync materials of assigned objects according to the probe’s settings (Boxprokjection only)
    public void SyncAssignedGameobjects() {
        // Get all objects
        for (int i = 0; i < AssignedMeshes.Count; i++)
        {
            if(AssignedMeshes[i].renderer != null)
            {
                int materials = AssignedMeshes[i].renderer.sharedMaterials.Length;
                // Get all materials
                for (int j = 0; j < materials; j++) {
                    if (AssignedMeshes[i].renderer.sharedMaterials[j].HasProperty("_CubemapPositionWS"))
                    {
                        AssignedMeshes[i].renderer.sharedMaterials[j].SetVector("_CubemapPositionWS", new Vector4(transform.position.x, transform.position.y, transform.position.z, 0));
                        AssignedMeshes[i].renderer.sharedMaterials[j].SetVector("_CubemapSize", new Vector4(BoxSize.x/2f, BoxSize.y, BoxSize.z/2f, 0));
                        if (SPECCube != null){
                            AssignedMeshes[i].renderer.sharedMaterials[j].SetTexture("_SpecCubeIBL", SPECCube);
                        }
                    }
                }
            }
            else {
                Debug.Log(AssignedMeshes[i].name+" does not have a mesh renderer attached to it. It has been removed.");
                AssignedMeshes.RemoveAt(i);
                break;
            }
        }
    }

    // Render Cubemaps using the built in function (pro only)
    public void RenderToCubeMap() {
        var cubeCamera = new GameObject( "CubemapCamera", typeof(Camera) ) as GameObject;
    //  cubeCamera.hideFlags = HideFlags.HideInHierarchy;
        var cubeCam = cubeCamera.GetComponent("Camera") as Camera;
        cubeCam.nearClipPlane = Near;
        cubeCam.farClipPlane = Far;
        cubeCam.aspect = 1.0f;
        cubeCam.cullingMask = CullingMask;
        cubeCam.clearFlags = ClearFlags;
        cubeCam.backgroundColor = ClearColor;

        cubeCamera.transform.position = transform.position;

        if (HDR == true)
        {
            cubeCam.hdr = true;
            texFor = TextureFormat.ARGB32;
        }
        else
        {
            cubeCam.hdr = false;
            texFor = TextureFormat.RGB24;
        }
        
        //  irradiance cubemap
        cubemap = new Cubemap((int)DiffSize, texFor, false);
        
        cubeCam.RenderToCubemap(cubemap);
        
        Cubemap diffCube = cubemap;
        diffCube.name = cubeName;
        if (SmoothEdges)
        {
            diffCube.SmoothEdges(SmoothEdgePixel);
        }
        diffCube.wrapMode = TextureWrapMode.Clamp;
        string finalDiffPath = diffCube.name + "DIFF.cubemap";
        AssetDatabase.CreateAsset(diffCube, finalDiffPath);
        SerializedObject serializedDiffCubemap = new SerializedObject(diffCube);
        SetLinearSpace(ref serializedDiffCubemap, true);
        DIFFCube = diffCube;

        // radiance cubemap
        cubemap = new Cubemap((int)SpecSize, texFor, true);

        cubeCam.RenderToCubemap(cubemap);
        Cubemap specCube = cubemap;
        specCube.name = cubeName;
        if (SmoothEdges)
        {
            specCube.SmoothEdges(SmoothEdgePixel);
        }
        specCube.wrapMode = TextureWrapMode.Clamp;
        string finalSpecPath = specCube.name + "SPEC.cubemap";
        AssetDatabase.CreateAsset(specCube, finalSpecPath);
        SerializedObject serializedSpecCubemap = new SerializedObject(specCube);
        SetLinearSpace(ref serializedSpecCubemap, true);
        SPECCube = specCube;

        GameObject.DestroyImmediate(cubeCamera);
    }




// ///////////////////////////////////


    public void InitRenderCube()
    {
        EditorApplication.isPlaying = true;
        StartCoroutine(InitializeStartSequence());     
    }

    IEnumerator RenderCubemap()
    {
      
        if (init && EditorApplication.isPlaying)
        {
            go = new GameObject("CubeCam");
            go.transform.position = transform.position;
            go.AddComponent<Camera>();
            CubeCamera = go.GetComponent<Camera>();
            CubeCamera.fieldOfView = 90;
            CubeCamera.nearClipPlane = Near;
            CubeCamera.farClipPlane = Far;
            CubeCamera.clearFlags = ClearFlags;
            CubeCamera.backgroundColor = ClearColor;
            CubeCamera.cullingMask = CullingMask;

            if (HDR == true)
            {
                CubeCamera.hdr = true;
                texFor = TextureFormat.ARGB32;
            }
            else
            {
                CubeCamera.hdr = false;
                texFor = TextureFormat.RGB24;
            }

            cubemap = new Cubemap((int)SpecSize, texFor, mipmap);
            StartCoroutine(RenderCubeFaces(cubemap, true));
            StartCoroutine(RenderCubeFaces(cubemap, false));
        }

        Resources.UnloadUnusedAssets();
        AssetDatabase.Refresh();
        yield return new WaitForEndOfFrame();

    }

    IEnumerator InitializeStartSequence()
    {
        StartCoroutine(RenderCubemap());
        init = true;
        yield return null;
    }

    IEnumerator RenderCubeFaces(Cubemap cube, bool irradiance)
    {
        if (irradiance)
        {
            size = (int)DiffSize;
            mipmap = false;
        }
        else
        {
            size = (int)SpecSize;
            mipmap = true;
        }

        cube = new Cubemap((int)size, texFor, mipmap);
        yield return StartCoroutine(Capture(cube, CubemapFace.PositiveZ, CubeCamera));
        yield return StartCoroutine(Capture(cube, CubemapFace.PositiveX, CubeCamera));
        yield return StartCoroutine(Capture(cube, CubemapFace.NegativeX, CubeCamera));
        yield return StartCoroutine(Capture(cube, CubemapFace.NegativeZ, CubeCamera));
        yield return StartCoroutine(Capture(cube, CubemapFace.PositiveY, CubeCamera));
        yield return StartCoroutine(Capture(cube, CubemapFace.NegativeY, CubeCamera));
        cube.Apply(mipmap);
        if(irradiance)
        {
            Cubemap diffCube = cube;
            diffCube.name = cubeName;
            if (SmoothEdges)
            {
                diffCube.SmoothEdges(SmoothEdgePixel);
            }

            diffCube.wrapMode = TextureWrapMode.Clamp;
            string finalDiffPath = diffCube.name + "DIFF.cubemap";

            AssetDatabase.CreateAsset(diffCube, finalDiffPath);
            SerializedObject serializedCubemap = new SerializedObject(diffCube);
            SetLinearSpace(ref serializedCubemap, true);
            DIFFCube = diffCube;
        }
        else
        {
            Cubemap specCube = cube;
            specCube.name = cubeName;
            if (SmoothEdges)
            {
                specCube.SmoothEdges(SmoothEdgePixel);
            }

            specCube.wrapMode = TextureWrapMode.Clamp;
            string finalSpecPath = specCube.name + "SPEC.cubemap";

            AssetDatabase.CreateAsset(specCube, finalSpecPath);

            SerializedObject serializedCubemap = new SerializedObject(specCube);
            SetLinearSpace(ref serializedCubemap, true);
            SPECCube = specCube;
        }
        yield return StartCoroutine(Finished());
    }

    IEnumerator Capture(Cubemap cubemap,CubemapFace face,Camera cam)
    {
	    var width = Screen.width;
	    var height = Screen.height;
	    Texture2D tex = new Texture2D(height, height, texFor, mipmap);
        int cubeSize = cubemap.height;

	    cam.transform.localRotation = RotationOf(face);
    
        yield return new WaitForEndOfFrame();

	    tex.ReadPixels(new Rect((width-height)/2, 0, height, height), 0, 0);
	    tex.Apply();
        tex = Scale(tex, cubeSize,cubeSize);

        Color cubeCol;
        for (int y = 0; y < cubeSize; y++)
        {
            for (int x = 0; x < cubeSize; x++)
            {
                cubeCol = tex.GetPixel(cubeSize + x, (cubeSize - 1) - y);
                if (Linear)
                {
                    cubeCol = cubeCol.linear;
                }
                cubemap.SetPixel(face, x, y, cubeCol);
            }
        }
        cubemap.Apply();
        DestroyImmediate(tex);
    }

    Quaternion RotationOf(CubemapFace face)
    {
        Quaternion result;
            switch(face)
	    {
		    case CubemapFace.PositiveX:
			    result = Quaternion.Euler(0,90,0);
		    break;
		    case CubemapFace.NegativeX:
			    result = Quaternion.Euler(0,-90,0);
		    break;
		    case CubemapFace.PositiveY:
			    result = Quaternion.Euler(-90,0,0);
		    break;
		    case CubemapFace.NegativeY:
			    result = Quaternion.Euler(90,0,0);
		    break;
		    case CubemapFace.NegativeZ:
			    result = Quaternion.Euler(0,180,0);
		    break;
		    default:
			    result = Quaternion.identity;
		    break;
	    }
	    return result;
    }

	// Update is called once per frame
	void Update () {

        if (UnityEditor.EditorApplication.isPlaying && init)
        {
            StartCoroutine(RenderCubemap());
            init = false;
        }
        else if (Done)
        {
            CleanUp();
            UnityEditor.EditorApplication.isPlaying = false;    
        }
        if (!UnityEditor.EditorApplication.isPlaying)
        {
            init = false;
        }
	}

    IEnumerator Finished()
    {
        Done = true;
        yield return null;
    }

    // Code taken from Jon-Martin.com
    static Texture2D Scale(Texture2D source ,int targetWidth ,int targetHeight )
    {
        Texture2D result = new Texture2D(targetWidth,targetHeight,source.format,true);
        Color[] rpixels = result.GetPixels(0);
        float incX = (1f/source.width)*((source.width *1f)/targetWidth);
        float incY = (1f/source.height)*((source.height * 1f)/targetHeight);
        for(var px = 0; px < rpixels.Length; px++)
        {
            rpixels[px] = source.GetPixelBilinear(incX*(px%targetWidth),incY*(Mathf.Floor(px/targetWidth)));
        }
        result.SetPixels(rpixels,0);
        result.Apply();
        return result;
    }

    void SetLinearSpace(ref SerializedObject obj, bool linear)
    {
        if (obj == null) return;

        SerializedProperty prop = obj.FindProperty("m_ColorSpace");
        if (prop != null)
        {
            prop.intValue = linear ? (int)ColorSpace.Gamma : (int)ColorSpace.Linear;
            obj.ApplyModifiedProperties();
        }
    }

    public void CleanUp()
    {
        //Camera[] cubeCams = FindObjectsOfType(typeof(Camera)) as Camera[];
        foreach (GameObject cubecam in GameObject.FindObjectsOfType<GameObject>())
        {
            if (cubecam.name == "CubeCam")
            {
                DestroyImmediate(cubecam);
            }
        }
        init = false;
    }

    public void RetriveCubemap()
    {
        DIFFCube = AssetDatabase.LoadAssetAtPath(DiffPath, (typeof(Cubemap))) as Cubemap;
        SPECCube = AssetDatabase.LoadAssetAtPath(SpecPath, (typeof(Cubemap))) as Cubemap;
    }
}
