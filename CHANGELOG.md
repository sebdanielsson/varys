# Changelog

## [0.2.0](https://github.com/sebdanielsson/varys/compare/Varys-v0.1.1...Varys-v0.2.0) (2026-06-08)


### Features

* **app:** first-run Ollama setup greeter ([a49cc67](https://github.com/sebdanielsson/varys/commit/a49cc6734bd293d63d406d766f7c33226d228409))
* **installer:** WiX MSI that installs to Program Files ([ef8ec19](https://github.com/sebdanielsson/varys/commit/ef8ec19e99a0213e05dbefb180ff405ff18cb790))
* **settings:** native Settings page (theme, language, about) ([383fe8c](https://github.com/sebdanielsson/varys/commit/383fe8c9d37e9600020af250da72a404bcb2c64e))
* **welcome:** env-gated preview of the full onboarding flow ([5270962](https://github.com/sebdanielsson/varys/commit/52709626d23d132705c0784a0fc368b04ab2699e))
* **welcome:** multi-step first-run setup (engine, Ollama, models) ([c1b2773](https://github.com/sebdanielsson/varys/commit/c1b277330e7108fc090430896857bc5c5b5726e8))
* **welcome:** require engine + voice model + Ollama + language model ([1b62b59](https://github.com/sebdanielsson/varys/commit/1b62b59e40151821159beb24c7623c95955fea97))


### Bug Fixes

* **notes:** seamless markdown preview via opaque surface match ([2d35972](https://github.com/sebdanielsson/varys/commit/2d35972f4f597415f5bbfd080b48f736735b7976))
* **notes:** transparent WebView + theme-reactive markdown preview ([7504050](https://github.com/sebdanielsson/varys/commit/75040507d90fe5400ce94dd04e0bc919ebd46d35))
* **packaging:** ship Assets in publish output (window icon) ([d00dc3d](https://github.com/sebdanielsson/varys/commit/d00dc3dea2ce3c9f08839d19fb941efa76703d72))
* **release:** force self-contained publish (.NET runtime was missing) ([46047d5](https://github.com/sebdanielsson/varys/commit/46047d5e69174947b44f7e24fcacddae56ae576b))
* **release:** pass absolute PublishDir to wix (empty MSI) ([6845f02](https://github.com/sebdanielsson/varys/commit/6845f02bcc75ebffb02ec7c0f05fd0a05ffdb400))
