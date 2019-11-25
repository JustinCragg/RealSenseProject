using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class Post : MonoBehaviour { 
    Material material;

	void Awake() {
        material = new Material(Shader.Find("Custom/PostSmoothing"));
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination) {
        Graphics.Blit(source, destination, material);
    }
}
