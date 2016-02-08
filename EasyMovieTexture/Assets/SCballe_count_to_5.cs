using UnityEngine;
using System.Collections;

public class SCballe_count_to_5 : MonoBehaviour {
	

	public float counter=0f;
	public GameObject looker;
	public float forcePower = 5f;


	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {

		counter += Time.deltaTime;
		if(counter>=15f) {
			//counter = 0f;
			Destroy (gameObject);
		}
	
	}
}
