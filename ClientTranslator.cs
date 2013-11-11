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
				ClientRequest clientRequest = ProcessHeader();
				clientRequest.IP = ((IPEndPoint)Client.Client.RemoteEndPoint).Address;
				return clientRequest;
			} catch (Exception e) {
				Console.WriteLine ("Failed to process client request: " + e);
				return null;
			}
		}

		private ClientRequest ProcessHeader() {
			ClientRequest CR = null;
			List<string> headerList = new List<string> ();
			string line = String.Empty;
			byte cb;
			while (true) {
				while(true) {
					int i = Communications.ReadByte ();
					if (i == -1) {
						break;
					}
					cb = Convert.ToByte (i);
					if(cb == '\n')
						break;
					if(cb == '\r')
						continue;
					line += Convert.ToChar (cb);
				}
				if(line == String.Empty) {
					break;
				} else {
					headerList.Add(String.Copy(line));
					line = String.Empty;
				}
			}
			CR = new ClientRequest (headerList);
			if (CR.ProcessRawHeader()) {
				int bodylength = CR.ContentLength;
				byte[] body = new byte[bodylength];
				Communications.Read (body, 0, bodylength);
				CR.SetBody (body);
			}
			return CR;
		}

		public void SendHTTP(HTTPObject H) {
			H.StreamTo (Communications);
		}
	}
}
