import 'package:flutter_web_auth_2/flutter_web_auth_2.dart';
import 'package:orvano_core/orvano_core.dart';

/// The default [OAuthLauncher] of a Flutter client (spec 0012, AC-22): the
/// system's auth session through `flutter_web_auth_2`, waiting for the browser
/// to come back on the redirect URL. A custom scheme (`com.acme.app://auth`)
/// works on every platform `flutter_web_auth_2` supports; an `https`
/// redirect URL is matched by host and path (iOS 17.4 and later). On Windows
/// and Linux the package listens on `localhost`, so make `localhost` a web
/// platform of the project.
Future<Uri> webAuthLauncher(Uri url, Uri redirectUrl) async {
  final https = redirectUrl.scheme == 'https';
  final result = await FlutterWebAuth2.authenticate(
    url: url.toString(),
    callbackUrlScheme: redirectUrl.scheme,
    options: FlutterWebAuth2Options(
      httpsHost: https ? redirectUrl.host : null,
      httpsPath: https ? redirectUrl.path : null,
    ),
  );
  return Uri.parse(result);
}
