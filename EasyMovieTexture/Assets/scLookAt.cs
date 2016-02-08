using UnityEngine;
using System.Collections;

public class scLookAt : MonoBehaviour {

	public GameObject objTarget;

	// Use this for initialization
	void Start () {
		objTarget = GameObject.Find ("target");
	
	}
	
	// Update is called once per frame
	void Update () {
	
	transform.LookAt (objTarget.transform);
	}
}
