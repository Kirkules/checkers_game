using System;
using System.Collections.Concurrent;
using Checkers.Messages;
using System.Net.Sockets;
using System.Runtime.Remoting.Lifetime;
using System.Runtime.Remoting.Messaging;
using System.Dynamic;
using System.Net;

namespace CheckersServer
{
	public class MessageDispatcher
	{
		public Connection RedPlayerConnection { get; private set; }
		public Connection WhitePlayerConnection { get; private set; }
		public CheckersMessage DummyMessage = null;
		public event EventHandler MessageReceived;

		protected virtual void OnMessageReceived(EventArgs e){
			EventHandler handler = MessageReceived;
			if (handler != null) {
				handler (this, e);
			}

		}

		public class MessageReceivedEventArgs : EventArgs {
			public CheckersMessage Message { get; set; }
		}


		public MessageDispatcher ()
		{
			DummyMessage = new CheckersMessage() {
				ProtocolVersion = 0
			};
			RedPlayerConnection = null;
			WhitePlayerConnection = null;
		}

		public bool RemoveDisconnectedPlayers(){
			bool someoneDisconnected = false;

			// send test message 
			if (RedPlayerConnection != null && !RedPlayerConnection.Client.Connected) {
				RedPlayerConnection = null;
				someoneDisconnected = true;
				Console.WriteLine ("Red player disconnected.");
			}
			if (WhitePlayerConnection != null && !WhitePlayerConnection.Client.Connected) {
				WhitePlayerConnection = null;
				someoneDisconnected = true;
				Console.WriteLine ("White player disconnected.");
			}
			return someoneDisconnected;
		}

		// returns true if chosen player was connected with the given client, false otherwise (including if
		// there is already a player connected to the given side)
		public bool ConnectPlayer(Side side, TcpClient client){
			if (side == Side.Red) {
				if (RedPlayerConnection != null) {
					Console.WriteLine ("already have red player conection");
					return false;
				} else {
					RedPlayerConnection = new Connection (client);
					return true;
				}
			} else if (side == Side.White) {
				if (WhitePlayerConnection != null) {
					return false;
				} else {
					WhitePlayerConnection = new Connection (client);
					return true;
				}
			} else {
				return false;
			}
		}

		public void SendMessage(TcpClient client, CheckersMessage message){
			Connection tempConnection = new Connection (client);
			tempConnection.SendMessage (message);
			tempConnection.Shutdown ();
		}

		public bool SendMessage(Side side, CheckersMessage message){
			if (side == Side.Red && PlayerConnected (Side.Red)) {
				RedPlayerConnection.SendMessage (message);
				return true;
			} else if (side == Side.White && PlayerConnected (Side.White)) {
				WhitePlayerConnection.SendMessage (message);
				return true;
			}
			return false;
		}


		public bool PlayerConnected(Side side){
			if (side == Side.Red) {
				if (RedPlayerConnection != null) {
					return RedPlayerConnection.Client.Connected;
				} else {
					return false;
				}
			} else if (side == Side.White) {
				if (WhitePlayerConnection != null) {
					return WhitePlayerConnection.Client.Connected;
				} else {
					return false;
				}
			}
			return false;
		}


		public string WhoIsPlayer(Side side){
			if (side == Side.Red) {
				return RedPlayerConnection.ToString ();
			} else if (side == Side.White) {
				return WhitePlayerConnection.ToString ();
			}
			return "Unknown player.";
		}


	}
}

