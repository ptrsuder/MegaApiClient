namespace CG.Web.MegaApiClient.Serialization
{
  using System;
  using System.Collections.Generic;
  using Newtonsoft.Json;

  internal class ImportFolderNodeRequest : RequestBase
  {
    private ImportFolderNodeRequest(string parentNodeId, string attributes, string encryptedKey, string publicLinkId)
      : base("p")
    {     
      Nodes = new List<ImportFolderNodeRequestData>
      {
        new ImportFolderNodeRequestData
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

    private ImportFolderNodeRequest(string parentNodeId)
      : base("p")
    {
      Nodes = new List<ImportFolderNodeRequestData>();
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
    public List<ImportFolderNodeRequestData> Nodes { get; private set; }

    public static ImportFolderNodeRequest ImportFileRequest(string parentNodeId, string attributes, string encryptedkey, string completionHandle)
    {
      return new ImportFolderNodeRequest(parentNodeId, attributes, encryptedkey, completionHandle);
    }

    public static ImportFolderNodeRequest ImportFolderRequest(string parentNodeId)
    {
      return new ImportFolderNodeRequest(parentNodeId);
    }

    //public static CreateNodeRequest CreateFolderNodeRequest(INode parentNode, string attributes, string encryptedkey, byte[] key)
    //{
    //  return new CreateNodeRequest(parentNode, NodeType.Directory, attributes, encryptedkey, key, "xxxxxxxx");
    //}

    internal class ImportFolderNodeRequestData
    {
      [JsonProperty("h")]
      public string PublicLinkId { get; set; }

      [JsonProperty("t")]
      public NodeType Type { get; set; }

      [JsonProperty("a")]
      public string Attributes { get; set; }

      [JsonProperty("k")]
      public string Key { get; set; }
    }

    internal class ImportFolderFileNodeRequestData: ImportFolderNodeRequestData
    {      
      [JsonProperty("p")]
      public string ParentId { get; set; }
    }
  }
}
