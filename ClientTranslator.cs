using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using Sabertooth.Lexicon;
using Sabertooth.HTTP;

namespace Sabertooth {
	public class ClientTranslator {
		TcpClient Client;
		BufferedStream Communications;
		public ClientTranslator(TcpClient client) {
			Client = client;
			Communications = new BufferedStream (client.GetStream ());
		}

		public void Close() {
		}

		public ClientRequest ProcessRequest() {
			try {
				ClientRequest clientRequest = new ClientRequest(new BinaryReader(Communications), ((IPEndPoint)Client.Client.RemoteEndPoint).Address);
				return clientRequest;
			} catch {
				//Console.WriteLine ("Failed to process client request: " + e);
				return null;
			}
		}

		public void SendHTTP(Response R, bool complete = true) {
			if (complete) {
				R.StreamCompleteTo (Communications);
			} else {
				R.StreamHeaderTo (Communications);
			}
		}
	}
}
