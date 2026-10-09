import 'package:flutter_test/flutter_test.dart';
import 'package:orvano_core/orvano_core.dart';
import 'package:orvano_flutter_passkeys/orvano_flutter_passkeys.dart';
import 'package:passkeys_platform_interface/passkeys_platform_interface.dart';
import 'package:passkeys_platform_interface/types/types.dart';

// Spec 0013 AC-38: PlatformPasskeys maps Orvano's WebAuthn options to the
// passkeys package's types and its answers back, field for field. The fake
// platform stands in for ASAuthorization, Credential Manager, and the web
// plugin; real devices are verify.md's job.

final class _FakePlatform extends PasskeysPlatform {
  RegisterRequestType? registered;
  AuthenticateRequestType? authenticated;
  String userHandle = 'dXNlcl8x';

  @override
  Future<RegisterResponseType> register(RegisterRequestType request) async {
    registered = request;
    return const RegisterResponseType(
      id: 'Y3JlZF8x',
      rawId: 'Y3JlZF8x',
      clientDataJSON: 'Y2xpZW50RGF0YQ',
      attestationObject: 'YXR0ZXN0YXRpb24',
      transports: ['internal', null, 'hybrid'],
    );
  }

  @override
  Future<AuthenticateResponseType> authenticate(
    AuthenticateRequestType request,
  ) async {
    authenticated = request;
    return AuthenticateResponseType(
      id: 'Y3JlZF8x',
      rawId: 'Y3JlZF8x',
      clientDataJSON: 'Y2xpZW50RGF0YQ',
      authenticatorData: 'YXV0aERhdGE',
      signature: 'c2lnbmF0dXJl',
      userHandle: userHandle,
    );
  }

  @override
  Future<void> cancelCurrentAuthenticatorOperation() async {}

  @override
  Future<AvailabilityType> getAvailability() =>
      throw UnimplementedError('not used by PlatformPasskeys');
}

final _creation = PasskeyCreationOptions.fromJson({
  'rp': {'id': 'example.com', 'name': 'Shop'},
  'user': {'id': 'dXNlcl8x', 'name': 'ada@example.com', 'displayName': 'Ada'},
  'challenge': 'Y2hhbGxlbmdlXzE',
  'pubKeyCredParams': [
    {'type': 'public-key', 'alg': -7},
    {'type': 'public-key', 'alg': -257},
  ],
  'timeout': 300000,
  'excludeCredentials': [
    {
      'type': 'public-key',
      'id': 'b2xkX2NyZWQ',
      'transports': ['internal'],
    },
  ],
  'authenticatorSelection': {
    'residentKey': 'required',
    'requireResidentKey': true,
    'userVerification': 'required',
  },
  'attestation': 'none',
});

final _request = PasskeyRequestOptions.fromJson({
  'challenge': 'Y2hhbGxlbmdlXzI',
  'rpId': 'example.com',
  'timeout': 300000,
  'userVerification': 'required',
  'allowCredentials': [
    {
      'type': 'public-key',
      'id': 'Y3JlZF8x',
      'transports': ['internal', 'hybrid'],
    },
  ],
});

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  late _FakePlatform platform;

  setUp(() => PasskeysPlatform.instance = platform = _FakePlatform());

  test('create maps the creation options and the attestation', () async {
    final made = await PlatformPasskeys().create(_creation);

    final asked = platform.registered!;
    expect(asked.challenge, 'Y2hhbGxlbmdlXzE');
    expect(asked.relyingParty.id, 'example.com');
    expect(asked.relyingParty.name, 'Shop');
    expect(asked.user.id, 'dXNlcl8x');
    expect(asked.user.name, 'ada@example.com');
    expect(asked.user.displayName, 'Ada');
    expect(asked.pubKeyCredParams?.map((p) => p.alg), [-7, -257]);
    expect(asked.timeout, 300000);
    expect(asked.excludeCredentials.single.id, 'b2xkX2NyZWQ');
    expect(asked.excludeCredentials.single.transports, ['internal']);
    expect(asked.authSelectionType?.residentKey, 'required');
    expect(asked.authSelectionType?.requireResidentKey, isTrue);
    expect(asked.authSelectionType?.userVerification, 'required');
    expect(asked.attestation, 'none');

    expect(made.id, 'Y3JlZF8x');
    expect(made.rawId, 'Y3JlZF8x');
    expect(made.type, 'public-key');
    expect(made.response.clientDataJSON, 'Y2xpZW50RGF0YQ');
    expect(made.response.attestationObject, 'YXR0ZXN0YXRpb24');
    expect(made.response.transports, ['internal', 'hybrid']);
  });

  test('get maps the request options and the assertion', () async {
    final used = await PlatformPasskeys().get(_request);

    final asked = platform.authenticated!;
    expect(asked.challenge, 'Y2hhbGxlbmdlXzI');
    expect(asked.relyingPartyId, 'example.com');
    expect(asked.timeout, 300000);
    expect(asked.userVerification, 'required');
    expect(asked.allowCredentials?.single.id, 'Y3JlZF8x');
    expect(asked.allowCredentials?.single.transports, ['internal', 'hybrid']);
    expect(asked.mediation, MediationType.Optional);

    expect(used.id, 'Y3JlZF8x');
    expect(used.rawId, 'Y3JlZF8x');
    expect(used.type, 'public-key');
    expect(used.response.clientDataJSON, 'Y2xpZW50RGF0YQ');
    expect(used.response.authenticatorData, 'YXV0aERhdGE');
    expect(used.response.signature, 'c2lnbmF0dXJl');
    expect(used.response.userHandle, 'dXNlcl8x');
  });

  test('autofill asks for conditional mediation', () async {
    await PlatformPasskeys().get(_request, autofill: true);

    expect(platform.authenticated!.mediation, MediationType.Conditional);
  });

  test('an empty user handle becomes null', () async {
    platform.userHandle = '';

    final used = await PlatformPasskeys().get(_request);

    expect(used.response.userHandle, isNull);
  });
}
