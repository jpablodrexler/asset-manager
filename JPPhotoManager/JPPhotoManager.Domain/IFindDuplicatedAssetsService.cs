﻿namespace JPPhotoManager.Domain
{
    public interface IFindDuplicatedAssetsService
    {
        List<List<Asset>> GetDuplicatedAssets();
    }
}