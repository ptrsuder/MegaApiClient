﻿namespace CG.Web.MegaApiClient.Serialization
{
  using System;
  using Newtonsoft.Json;

  internal class ImportNodeRequest : RequestBase
  { 
    private ImportNodeRequest(string parentNodeId, string attributes, string encryptedKey, string publicLinkId)
      : base("p")
    {     
      Nodes = new[]
      {
        new ImportNodeRequestData
        {
          Attributes = attributes,
          Key = encryptedKey,
          Type = NodeType.File,
          PublicLinkId = publicLinkId
        }
      };
      ParentId = parentNodeId;     
      
      //if (!(parentNode is INodeCrypto parentNodeCrypto))
      //{
      //  throw new ArgumentException("parentNode node must implement INodeCrypto");
      //}

      //if (parentNodeCrypto.SharedKey != null)
      //{
      //  Share = new ShareData(parentNode.Id);
      //  Share.AddItem(completionHandle, key, parentNodeCrypto.SharedKey);
      //}
    }

    [JsonProperty("t")]
    public string ParentId { get; private set; }

    [JsonProperty("cr")]
    public ShareData Share { get; private set; }

    [JsonProperty("n")]
    public ImportNodeRequestData[] Nodes { get; private set; }

    public static ImportNodeRequest ImportFileNodeRequest(string parentNodeId, string attributes, string encryptedkey, string completionHandle)
    {
      return new ImportNodeRequest(parentNodeId, attributes, encryptedkey, completionHandle);
    }

    internal class ImportNodeRequestData
    {
      [JsonProperty("ph")]
      public string PublicLinkId { get; set; }

      [JsonProperty("t")]
      public NodeType Type { get; set; }

      [JsonProperty("a")]
      public string Attributes { get; set; }

      [JsonProperty("k")]
      public string Key { get; set; }
    }
}
}
