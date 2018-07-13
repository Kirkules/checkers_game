using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Remoting.Lifetime;
using Checkers.Messages;
using System.Collections.Concurrent;
using System.Threading;
using Google.Protobuf;
using System.Collections.Generic;
using System.Net;
using CheckersServer;


public class Connection
{
	private ConcurrentQueue<CheckersMessage> ToSend { get; set; }
	public ConcurrentQueue<CheckersMessage> ReceivedMessages { get; private set; }
	private MemoryStream ReadBuffer { get; set; }
	private MessageParser<CheckersMessage> parser { get; set; }
	private Thread WritingThread { get; set; }
	private Thread ReadingThread { get; set; }

	public TcpClient Client { get; private set; }
	public string Host { get; private set; } // who connected?

	// connect to a given host/port
	public Connection (string host, int port) : this(new TcpClient(host, port)){
	}

	// connect with an existing tcpclient
	public Connection (TcpClient client)
	{
		Client = client;
		ToSend = new ConcurrentQueue<CheckersMessage> ();
		ReceivedMessages = new ConcurrentQueue<CheckersMessage> ();
		ReadBuffer = new MemoryStream ();
		parser = new MessageParser<CheckersMessage> (() => new CheckersMessage());

		// Write things from the write buffer
		WritingThread = new Thread(WriteMessages);
		WritingThread.Start ();

		// Fill up the messages that were read
		ReadingThread = new Thread(ReadMessages);
		ReadingThread.Start ();
	}

	// Lives in a thread!
	public void WriteMessages() {
		CheckersMessage nextMessage;
		while (Client.Connected) {
			// spin until there's a message to send
			while (!ToSend.TryDequeue (out nextMessage)) {
				Thread.Sleep (100);
			}
			if (Client.Connected){ // could have disconnected in the interim between waiting for a message
				Google.Protobuf.MessageExtensions.WriteDelimitedTo (nextMessage, Client.GetStream());
			}
		}
	}

	// Lives in a thread!
	public void ReadMessages() {
		//CheckersMessage nextMessage;
		NetworkStream netStream = Client.GetStream();
		int bytesRead = 0;
		byte[] bufferFiller = new byte[2048]; // 2048 is just the read batch size, doesn't really matter how big it is
		while (Client.Connected){

			// fill up the read buffer. okay to block here!
			while (netStream.DataAvailable){
				
				// bufferFiller is just an intermediate data location, so overwriting it is fine.
				bytesRead = netStream.Read(bufferFiller, 0, bufferFiller.Length);
				ReadBuffer.Write(bufferFiller, 0, bytesRead);
				ReadBuffer.Seek (0, SeekOrigin.Begin);
			}

			// get a message if there is one. If there's an InvalidProtocolBufferException, trust/hope
			// that it happened because a delimited message was only partially transmitted upon
			// calling ParseDelimitedFrom

			try{
				ReceivedMessages.Enqueue(parser.ParseDelimitedFrom(ReadBuffer));
				//nextMessage = parser.ParseDelimitedFrom(ReadBuffer);
				//ReceivedMessages.Enqueue(nextMessage);
				ClearReadBufferBeforeCurrentPosition();
			} catch (InvalidProtocolBufferException){
				// Message wasn't ready; 
				//Console.WriteLine (e.Message);
			}
			// don't check too frequently
			Thread.Sleep (100);
		}
	}

	private void ClearReadBufferBeforeCurrentPosition() {
		MemoryStream TempStream = new MemoryStream();
		ReadBuffer.CopyTo(TempStream);
		ReadBuffer.Dispose();
		ReadBuffer = TempStream;
	}

	public void SendMessage(CheckersMessage message){
		Debugging.Print ("Sending message " + message.ToString ());
		ToSend.Enqueue (message);
	}

	public void Shutdown(){
		Client.Client.Disconnect (false);
	}

	public override string ToString ()
	{
		string result = "";
		if (Client.Connected) {
			IPEndPoint endpoint = Client.Client.RemoteEndPoint as IPEndPoint;
			result += endpoint.Address + " on port " + endpoint.Port + ".";
		} else {
			result += "(Connection not connected).";
		}
		return result;
	}
}