using UnityEngine;
using System.Collections;
using System;
using System.Net.Sockets;

public class InitGame : MonoBehaviour {
	
	// prefabs
	public GameObject BoardPrefab;
	private GameObject BoardGameObject;
	private GameBoard Board;

	// how to connect to server
	public string host;
	public int port;


	// Use this for initialization
	void Start () {
		BoardGameObject = GameObject.Instantiate (BoardPrefab);
		Board = BoardGameObject.GetComponent<GameBoard> ();
	}
	
	// try to connect to server if not connected
	void Update () {
		if (Board.ServerConnection == null) {
			TryToConnectToServer ();
		} else {
			if (!Board.ServerConnection.Client.Connected) {
				TryToConnectToServer ();
			}
		}
	}

	public void TryToConnectToServer(){
		try {
			Board.Connect (host, port);
		} catch (SocketException) {
			// Didn't connect to the server. Trying again!
		}
	}
}
