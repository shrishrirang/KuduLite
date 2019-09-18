using Kudu.Contracts.SourceControl;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Kudu.Core.SourceControl
{
    class RunningSiteRepository : IRepository
    {
        public string CurrentId => throw new NotImplementedException();

        public string RepositoryPath => throw new NotImplementedException();

        public RepositoryType RepositoryType => throw new NotImplementedException();

        public bool Exists => throw new NotImplementedException();

        public void AddFile(string path)
        {
            throw new NotImplementedException();
        }

        public void Clean()
        {
            throw new NotImplementedException();
        }

        public void ClearLock()
        {
            throw new NotImplementedException();
        }

        public bool Commit(string message, string authorName, string emailAddress)
        {
            throw new NotImplementedException();
        }

        public void CreateOrResetBranch(string branchName, string startPoint)
        {
            throw new NotImplementedException();
        }

        public bool DoesBranchContainCommit(string branch, string commit)
        {
            throw new NotImplementedException();
        }

        public void FetchWithoutConflict(string remote, string branchName)
        {
            throw new NotImplementedException();
        }

        public ChangeSet GetChangeSet(string id)
        {
            throw new NotImplementedException();
        }

        public void Initialize()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> ListFiles(string path, SearchOption searchOption, params string[] lookupList)
        {
            throw new NotImplementedException();
        }

        public void Push()
        {
            throw new NotImplementedException();
        }

        public bool Rebase(string branchName)
        {
            throw new NotImplementedException();
        }

        public void RebaseAbort()
        {
            throw new NotImplementedException();
        }

        public void Update(string id)
        {
            throw new NotImplementedException();
        }

        public void Update()
        {
            throw new NotImplementedException();
        }

        public void UpdateRef(string source)
        {
            throw new NotImplementedException();
        }

        public void UpdateSubmodules()
        {
            throw new NotImplementedException();
        }
    }
}
