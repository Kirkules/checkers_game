using UnityEngine;
using System.Collections;

public class Debugging {

	public static bool On = true;
	public static void Print(string toPrint){
		if (On) {
			Debug.Log (toPrint);
		}
	}
}
