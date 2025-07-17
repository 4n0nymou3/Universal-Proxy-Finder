# Universal Proxy Finder

An intelligent, self-updating tool that automatically finds, tests, and delivers fresh proxy configurations from various online sources.

This repository is designed to be fully automated using GitHub Actions. It fetches the latest version of the Sing-box core, gathers proxy links, tests them for connectivity, and commits the working ones back to this repository.

## How It Works

1.  **Collect**: Gathers proxy links from a predefined list of sources.
2.  **Test**: Uses the latest version of `sing-box` to perform a real-world connectivity test on each proxy.
3.  **Format**: Renames working proxies with a clear convention: `Anonymous-FLAG-COUNTRYCODE-PROTOCOL-INDEX`.
4.  **Deploy**: Commits the final subscription files to the repository.

This entire process runs automatically every 24 hours.

## Subscription Links

You can use these links in your preferred client application (like v2rayNG, Nekoray, Streisand, etc.).

### V2Ray Subscription Link:
```
https://raw.githubusercontent.com/imegabiz/Universal-Proxy-Finder/master/Output/v2ray_subscription.txt
```

### Sing-box Subscription Link:
```
https://raw.githubusercontent.com/imegabiz/Universal-Proxy-Finder/master/Output/singbox_subscription.json
```

> **Note**: Replace `YOUR_USERNAME` with your actual GitHub username after forking this repository.

## Acknowledgments

Special thanks to:
[Mahdi0024](https://github.com/Mahdi0024)

Free internet for everyone ❤️