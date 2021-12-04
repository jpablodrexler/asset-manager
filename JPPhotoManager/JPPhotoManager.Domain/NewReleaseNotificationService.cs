﻿using JPPhotoManager.Domain.Interfaces;
using log4net;
using System.Reflection;

namespace JPPhotoManager.Domain
{
    public class NewReleaseNotificationService : INewReleaseNotificationService
    {
        private readonly IUserConfigurationService userConfigurationService;
        private readonly IReleaseAvailabilityService releaseAvailabilityService;

        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public NewReleaseNotificationService(IUserConfigurationService userConfigurationService,
            IReleaseAvailabilityService releaseAvailabilityService)
        {
            this.userConfigurationService = userConfigurationService;
            this.releaseAvailabilityService = releaseAvailabilityService;
        }

        public async Task<Release> CheckNewRelease()
        {
            Release latestRelease = null;

            try
            {
                var aboutInformation = this.userConfigurationService.GetAboutInformation(this.GetType().Assembly);
                latestRelease = await this.releaseAvailabilityService.GetLatestRelease();

                if (aboutInformation != null && latestRelease != null)
                {
                    latestRelease.IsNewRelease = IsNewRelease(aboutInformation.Version, latestRelease.Name);
                    latestRelease.Success = true;
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }

            return latestRelease ?? new Release();
        }

        private bool IsNewRelease(string currentVersion, string latestReleaseName)
        {
            bool result = !string.IsNullOrEmpty(currentVersion) && !string.IsNullOrEmpty(latestReleaseName);

            if (result)
            {
                var currentVersionNumbers = GetVersionNumbers(currentVersion);
                var latestReleaseNumbers = GetVersionNumbers(latestReleaseName);
                result = currentVersionNumbers.isValid && latestReleaseNumbers.isValid;

                if (result)
                {
                    result = latestReleaseNumbers.major > currentVersionNumbers.major ||
                        (latestReleaseNumbers.major == currentVersionNumbers.major
                            && latestReleaseNumbers.minor > currentVersionNumbers.minor) ||
                        (latestReleaseNumbers.major == currentVersionNumbers.major
                            && latestReleaseNumbers.minor == currentVersionNumbers.minor
                            && latestReleaseNumbers.build > currentVersionNumbers.build);
                }
            }

            return result;
        }

        private (bool isValid, int major, int minor, int build) GetVersionNumbers(string version)
        {
            int major, minor = 0, build = 0;
            var parts = version.Substring(1).Split(new[] { '.' });
            bool isValid = int.TryParse(parts[0], out major)
                && int.TryParse(parts[1], out minor)
                && int.TryParse(parts[2], out build);

            return (isValid, major, minor, build);
        }
    }
}