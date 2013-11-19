using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using Sabertooth.Lexicon;
using Sabertooth.Mandate;
using Sabertooth.HTTP;

namespace Sabertooth {
	internal class Server {
		private bool Stopping = false;
		private TcpListener ClientListener;
		private Thread ClientProcessor;
		private MandateManager Gen;

		public Server() {
			ClientListener = new TcpListener (IPAddress.Any, 8080);
			Gen = new MandateManager();
		}
		public void Start() {
			Gen.Begin ();
			ClientListener.Start ();
			ClientProcessor = new Thread (ClientGateway);
			ClientProcessor.Start ();
		}
		public void Stop()  {
			Stopping = true;
			ClientListener.Stop ();
			Gen.End ();
		}
		private void ClientGateway () {
			while (!Stopping) {
				try {
					TcpClient newClient = ClientListener.AcceptTcpClient ();
					Thread clientSend = new Thread(() => ClientConcierge(newClient));
					clientSend.Start();
				} catch (Exception e) {
					Console.WriteLine ("A severe error has occured during client processing: {0}", e);
				}
			}
		}
		private void ClientConcierge (TcpClient Client) {
			ClientTranslator Concierge = new ClientTranslator (Client);
			//Concierge.SendHTTP (Response.Standard100);
			while(true){
				ClientRequest clientInfo = Concierge.ProcessRequest ();
				if (clientInfo == null) {
					break;
				}
				Response R = GenerateResponse (clientInfo);
				Concierge.SendHTTP (R);
			}
			Concierge.Close ();
			Client.Close ();
		}

		private Response GenerateResponse(ClientRequest CR) {
			if (CR.Host == null) {
				Response BR = new Response (Response.Code.N400, new TextResource("Sabertooth requires that clients use HTTP 1.1 connections, and thus have Host fields in their headers."));
				BR.connectionStatus = Instruction.Connection.Close;
				return BR;
			}
			if (!(CR.Type == "GET" || CR.Type == "POST" || CR.Type == "HEAD")) {
				Response BR = new Response (Response.Code.N400, new TextResource ("Sabertooth only supports GET, POST, and HEAD at the moment."));
				return BR;
			}
			string realm;
			Tuple<string, string> auth = CR.Authorization;
			if (!Gen.IsAuthorized(CR, auth, out realm)) {
				EmptyResponse AUTH = new EmptyResponse (Response.Code.N401);
				AUTH.AddInstruction (Instruction.Authenticate(realm));
				return AUTH;
			}
			Response Res = new Response (Response.Code.N200, new TextResource("You should not be seeing this."));
			try {
				ClientBody CB = CR.ReadBody();
				IStreamableContent GenRes;
				if (CB == null) {
					GenRes = Gen.Get(CR);
				} else {
					GenRes = Gen.Post(CR, CB);
				}
				if (GenRes == null)
					throw new ArgumentNullException("The mandate failed to generate a page, hopefully an error log appears above.");
				Res.SetBody (GenRes);
			} catch (Exception e) {
				Console.WriteLine (e);
				Response ISE = new Response (Response.Code.N500, new TextResource ("If you are reading this text, the Sabertooth mandate responsible for this request has encountered an error. If you are the server administrator, an error log should have appeared in the main Sabertooth console window."));
				ISE.connectionStatus = Instruction.Connection.Close;
				return ISE;
			}
			if (CR.Type == "HEAD")
				return (EmptyResponse)Res;
			return Res;
		}

		public static string GetPath(string URLPath) {
			return Path.Combine (Environment.CurrentDirectory, "Assets", URLPath);
		}
	}
}

