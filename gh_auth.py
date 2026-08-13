import requests, time, sys, os

# Step 1: Request device code
r = requests.post('https://github.com/login/device/code',
    data={'client_id': 'L8WJq3bM8v7dK5v2p2sZ', 'scope': 'repo,read:org'},
    headers={'Accept': 'application/json'})
data = r.json()
print('device_code:', data['device_code'][:20]+'...')
print('user_code:', data['user_code'])
print('verification_uri:', data['verification_uri'])
print()
print('Enter this code at', data['verification_uri'], ':')
print('******', data['user_code'], '******')
print()
print('Polling for authorization...')

# Step 2: Poll for access token
interval = data.get('interval', 5)
device_code = data['device_code']
token = None
for i in range(60):
    time.sleep(interval)
    try:
        r2 = requests.post('https://github.com/login/oauth/access_token',
            data={'client_id': 'L8WJq3bM8v7dK5v2p2sZ', 'device_code': device_code, 'grant_type': 'urn:ietf:params:oauth:grant-type:device_code'},
            headers={'Accept': 'application/json'})
        resp = r2.json()
        if 'access_token' in resp:
            token = resp['access_token']
            print()
            print('TOKEN ACQUIRED:', token[:20]+'...')
            break
        elif resp.get('error') == 'authorization_pending':
            print('.', end='', flush=True)
        elif resp.get('error') == 'slow_down':
            interval += 5
            print('s', end='', flush=True)
        else:
            print()
            print('ERROR:', resp)
            break
    except Exception as e:
        print()
        print('Exception:', e)
        break

if token:
    # Write token for gh
    import subprocess
    result = subprocess.run(['gh', 'auth', 'login', '--with-token'], input=token, capture_output=True, text=True)
    print('gh auth login exit:', result.returncode)
    print('stdout:', result.stdout)
    print('stderr:', result.stderr)
    
    # Verify
    result2 = subprocess.run(['gh', 'auth', 'status'], capture_output=True, text=True)
    print('gh auth status:', result2.stdout + result2.stderr)
    sys.exit(0)
else:
    print()
    print("Timeout - user didn't authorize in time")
    sys.exit(1)