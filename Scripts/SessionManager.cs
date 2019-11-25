using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SessionManager : MonoBehaviour {
    PXCMSenseManager psm = null;

    PXCMHandModule handModule = null;
    PXCMHandData handData = null;
    PXCMHandConfiguration handConfig = null;

    Texture2D sampleTexture = null;
    public Image sampleImage = null;
    public Text depthText = null;

    List<GameObject> hands = new List<GameObject>();

    public GameObject depthMeshObject = null;

    Camera mainCamera = null;

    public int currentEffect = 0;
    public List<GameObject> effects = new List<GameObject>();

    bool showColour = false;
    float gestureCooldown = 0;

	void Start() {
        // Initialise a PXCMSenseManager instance
        psm = PXCMSenseManager.CreateInstance();
        if (psm == null) {
            Debug.LogError("SenseManager Init Failed");
            return;
        }

        // Enable the depth and colour streams
        psm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH, 640, 480);
        psm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, 640, 480);

        // Enable hand analysis
        pxcmStatus sts = psm.EnableHand();
        if (sts != pxcmStatus.PXCM_STATUS_NO_ERROR) {
            Debug.LogError("SenseManager Hand Init Failed");
            OnDisable();
            return;
        }
        handModule = psm.QueryHand();

        // Initialise the execution pipeline
        sts = psm.Init();
        if (sts != pxcmStatus.PXCM_STATUS_NO_ERROR) {
            Debug.LogError("SenseManager Pipeline Init Failed");
            OnDisable();
            return;
        }

        handData = handModule.CreateOutput();

        handConfig = handModule.CreateActiveConfiguration();
        handConfig.EnableAllGestures();
        handConfig.ApplyChanges();

        foreach (CapsuleCollider capsule in GetComponentsInChildren<CapsuleCollider>()) {
            hands.Add(capsule.gameObject);
        }

        mainCamera = GetComponentInChildren<Camera>();
    }

    void Update() {
        if (gestureCooldown > 0) {
            gestureCooldown -= Time.deltaTime;
        }

        if (psm == null) {
            return;
        }
        if (psm.AcquireFrame(true) != pxcmStatus.PXCM_STATUS_NO_ERROR) {
            return;
        }

        drawCameraVision();

        handData.Update();

        if (currentEffect == 2) {
            PXCMHandData.JointData[,] nodes = getHandInfo();
            followJoints(nodes);
        }
        else if (currentEffect == 3) {
            // point cloud
            createDepthMesh();
        }

        moveHands();
        handleGestures();

        // Release the frame
        psm.ReleaseFrame();
    }

    void drawCameraVision() {
        // Camera Sample
        PXCMCapture.Sample sample = getCameraSample();
        // Depth Texture
        if (showColour == false) {
            textureFromSample(sample.depth);
        }
        else {
            textureFromSample(sample.color);
        }
    }

    void textureFromSample(PXCMImage camSample) {
        if (sampleTexture == null) {
            sampleTexture = new Texture2D(camSample.info.width, camSample.info.height, TextureFormat.ARGB32, false);

            sampleImage.material.mainTexture = sampleTexture;
            sampleImage.rectTransform.localScale = new Vector3(-1, 1, 1);
        }

        PXCMImage.ImageData imageData;
        camSample.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out imageData);
        imageData.ToTexture2D(0, sampleTexture);
        camSample.ReleaseAccess(imageData);

        sampleTexture.Apply();
    }

    PXCMCapture.Sample getCameraSample() {

        // Retrieve sample from camera
        PXCMCapture.Sample sample = psm.QuerySample();

        return sample;
    }

    PXCMHandData.JointData[,] getHandInfo() {
        int handNum = handData.QueryNumberOfHands();

        PXCMHandData.JointData[,] nodes = new PXCMHandData.JointData[handNum, PXCMHandData.NUMBER_OF_JOINTS];

        // Iterate through hands
        for (int i = 0; i < handNum; i++) {
            // Get hand joints by time of appearance
            PXCMHandData.IHand ihandData;
            if (handData.QueryHandData(PXCMHandData.AccessOrderType.ACCESS_ORDER_BY_TIME, i, out ihandData) == pxcmStatus.PXCM_STATUS_NO_ERROR) {
                for (int j = 0; j < PXCMHandData.NUMBER_OF_JOINTS; j++) {
                    ihandData.QueryTrackedJoint((PXCMHandData.JointType)j, out nodes[i, j]);
                }
            }
        }

        return nodes;
    }

    void followJoints(PXCMHandData.JointData[,] nodes) {
        List<ParticleSystem> systems = new List<ParticleSystem>(effects[currentEffect].GetComponentsInChildren<ParticleSystem>());
        for (int i = 0; i < nodes.Length; i++) {
            PXCMPoint3DF32 position = nodes[0, i].positionWorld;
            systems[i].transform.position = new Vector3(-position.x * 25, position.y * 25 - 0, -position.z * 7.5f + 2.5f);
        }
    }

    void moveHands() {
        for (int i = 0; i < hands.Count; i++) {
            if (i < handData.QueryNumberOfHands()) {
                hands[i].SetActive(true);

                PXCMHandData.IHand ihandData;
                if (handData.QueryHandData(PXCMHandData.AccessOrderType.ACCESS_ORDER_BY_TIME, i, out ihandData) == pxcmStatus.PXCM_STATUS_NO_ERROR) {
                    PXCMPoint3DF32 handPos = ihandData.QueryMassCenterWorld();
                    PXCMPoint4DF32 handRot = ihandData.QueryPalmOrientation();
                    float handRadius = ihandData.QueryPalmRadiusWorld();
                    PXCMRectI32 handBounds = ihandData.QueryBoundingBoxImage();
                    hands[i].transform.position = new Vector3(-handPos.x * 25, handPos.y * 25, -handPos.z * 25 + 10);
                    depthText.text = "Hand Depth: " +  -Mathf.RoundToInt(hands[i].transform.position.z*100) / 100.0f;
                    hands[i].transform.rotation = new Quaternion(handRot.x, handRot.y, handRot.z, handRot.w);
                    hands[i].transform.localScale = new Vector3(handBounds.h, handBounds.h, handBounds.h * 2) * 0.0075f;
                }
            }
            else {
                hands[i].SetActive(false);
            }
        }
    }

    void handleGestures() {
        PXCMHandData.GestureData gestureData;

        for (int i = 0; i < handData.QueryFiredGesturesNumber(); i++) {
            if (handData.QueryFiredGestureData(i, out gestureData) == pxcmStatus.PXCM_STATUS_NO_ERROR) {
                if (gestureCooldown <= 0) {
                    if (gestureData.name == "v_sign") {
                        showColour = !showColour;
                        gestureCooldown = 2.5f;
                    }
                    else if (gestureData.name == "thumb_down") {
                        nextEffect();
                        gestureCooldown = 2.5f;
                    }
                }
            }
        }
    }
    
    void createDepthMesh() {
        PXCMImage depthImage = getCameraSample().depth;
        Texture2D tempTexture = new Texture2D(depthImage.info.width, depthImage.info.height, TextureFormat.ARGB32, false);

        PXCMImage.ImageData imageData;
        depthImage.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out imageData);
        imageData.ToTexture2D(0, tempTexture);
        depthImage.ReleaseAccess(imageData);

        Mesh depthMesh = depthMeshObject.GetComponent<MeshFilter>().mesh;
        depthMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        depthMesh.Clear();
        int width = tempTexture.width;
        int height = tempTexture.height;

        Vector3[] verts = new Vector3[(width + 1) * (height + 1)];
        //Vector2[] uvs = new Vector2[verts.Length];
        //Vector4[] tangents = new Vector4[verts.Length];
        //Vector4 tangent = new Vector4(0, 0, -1.0f, -1.0f);
        
        for (int i = 0, y = 0; y <= height; y++) {
            for (int x = 0; x <= width; x++, i++) {
                verts[i] = new Vector3(-width * 0.5f + x, -height * 0.5f + y, -tempTexture.GetPixel(x, y).grayscale * 100.0f) / 10.0f;
                //uvs[i] = new Vector2(x / (float)width, y / (float)height);
                //tangents[i] = tangent;
            }
        }
        depthMesh.vertices = verts;
        //depthMesh.uv = uvs;
        //depthMesh.tangents = tangents;

        int[] triangles = new int[width * height * 6];
        for (int ti = 0, vi = 0, y = 0; y < height; y++, vi++) {
            for (int x = 0; x < width; x++, ti += 6, vi++) {
                triangles[ti] = vi;
                triangles[ti + 3] = triangles[ti + 2] = vi + 1;
                triangles[ti + 4] = triangles[ti + 1] = vi + width + 1;
                triangles[ti + 5] = vi + width + 2;
            }
        }
        depthMesh.triangles = triangles;
        depthMesh.RecalculateNormals();
    }

    void nextEffect() {
        effects[currentEffect].SetActive(false);

        currentEffect++;
        if (currentEffect >= effects.Count) {
            currentEffect = 0;
        }

        effects[currentEffect].SetActive(true);
    }

    void OnDisable() {
        if (handConfig != null) {
            handConfig.Dispose();
        }

        if (handData != null) {
            handData.Dispose();
        }

        if (psm != null) {
            psm.Dispose();
        }
    }
}
