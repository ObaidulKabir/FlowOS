import React, { useState } from 'react';
import { AuthSession, RegisterTenantUserRequest } from '../types';
import { api, setAuthSession } from '../api/client';
import { 
  Building2, Shield, Mail, Lock, User, Key, ArrowRight, 
  Sparkles, CheckCircle2, AlertCircle, X, RefreshCw, Eye, EyeOff
} from 'lucide-react';

export type AuthModalMode = 'login' | 'register' | 'verify' | 'admin';

interface AuthModalProps {
  isOpen: boolean;
  initialMode?: AuthModalMode;
  onClose: () => void;
  onSuccess: (session: AuthSession) => void;
  onLaunchSandbox: () => void;
}

export const AuthModal: React.FC<AuthModalProps> = ({
  isOpen,
  initialMode = 'login',
  onClose,
  onSuccess,
  onLaunchSandbox
}) => {
  const [mode, setMode] = useState<AuthModalMode>(initialMode);

  // Sync mode if initialMode changes
  React.useEffect(() => {
    setMode(initialMode);
  }, [initialMode, isOpen]);

  // Login form state
  const [loginType, setLoginType] = useState<'credentials' | 'apiKey'>('credentials');
  const [loginEmail, setLoginEmail] = useState('');
  const [loginPassword, setLoginPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [loginApiKey, setLoginApiKey] = useState('flowos_prod_secret_key_32_chars_min');
  const [loginTenantId, setLoginTenantId] = useState('22222222-2222-2222-2222-222222222222');

  // Register form state
  const [regOrgName, setRegOrgName] = useState('');
  const [regFullName, setRegFullName] = useState('');
  const [regEmail, setRegEmail] = useState('');
  const [regPassword, setRegPassword] = useState('');
  const [regConfirmPassword, setRegConfirmPassword] = useState('');

  // Verification state
  const [verifyEmail, setVerifyEmail] = useState('');
  const [verifyToken, setVerifyToken] = useState('');
  const [devToken, setDevToken] = useState<string | null>(null);

  // Admin form state
  const [adminUsername, setAdminUsername] = useState('superadmin@flowos.internal');
  const [adminPassword, setAdminPassword] = useState('flowos-admin-root');

  // UI state
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  if (!isOpen) return null;

  const resetMessages = () => {
    setError(null);
    setSuccessMessage(null);
  };

  const handleLoginSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    resetMessages();

    if (loginType === 'apiKey') {
      if (!loginTenantId.trim()) {
        setError('Tenant ID is required.');
        return;
      }
      const session: AuthSession = {
        role: 'Tenant',
        tenantId: loginTenantId.trim(),
        tenantName: 'Client Tenant',
        apiKey: loginApiKey.trim() || undefined,
        username: `api-user@${loginTenantId.substring(0, 8)}.flowos`,
        isSandbox: false,
        isEmailVerified: true
      };
      setAuthSession(session);
      onSuccess(session);
      onClose();
      return;
    }

    if (!loginEmail.trim() || !loginPassword.trim()) {
      setError('Please enter both email and password.');
      return;
    }

    setLoading(true);
    try {
      const res = await api.loginTenantUser({
        email: loginEmail.trim(),
        password: loginPassword
      });

      if (!res.ok) {
        if (res.errorCode === 'EMAIL_NOT_VERIFIED') {
          setVerifyEmail(loginEmail.trim());
          setError('Email is not verified yet. Please enter the verification code sent from admin@flowosbd.com.');
          setMode('verify');
          return;
        }
        setError(res.message || 'Login failed. Please verify your credentials.');
        return;
      }

      if (res.user && res.token) {
        const session: AuthSession = {
          role: 'Tenant',
          tenantId: res.user.tenantId,
          tenantName: res.user.tenantName,
          token: res.token,
          username: res.user.fullName || res.user.email,
          email: res.user.email,
          isSandbox: false,
          isEmailVerified: res.user.isEmailVerified
        };
        setAuthSession(session);
        onSuccess(session);
        onClose();
      }
    } catch (err: any) {
      setError(err.message || 'Login request failed.');
    } finally {
      setLoading(false);
    }
  };

  const handleRegisterSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    resetMessages();

    if (!regOrgName.trim()) {
      setError('Organization name is required.');
      return;
    }
    if (!regEmail.trim()) {
      setError('Business email is required.');
      return;
    }
    if (regPassword.length < 8) {
      setError('Password must be at least 8 characters long.');
      return;
    }
    if (regPassword !== regConfirmPassword) {
      setError('Passwords do not match.');
      return;
    }

    setLoading(true);
    try {
      const req: RegisterTenantUserRequest = {
        tenantName: regOrgName.trim(),
        fullName: regFullName.trim() || undefined,
        email: regEmail.trim(),
        password: regPassword
      };

      const res = await api.registerTenantUser(req);
      if (!res.ok) {
        setError(res.message || 'Registration failed.');
        return;
      }

      setVerifyEmail(res.email || regEmail.trim());
      if (res.verificationToken) {
        setDevToken(res.verificationToken);
        setVerifyToken(res.verificationToken);
      }
      setSuccessMessage('Tenant account created! A 6-digit verification code has been dispatched from admin@flowosbd.com.');
      setMode('verify');
    } catch (err: any) {
      setError(err.message || 'Registration request failed.');
    } finally {
      setLoading(false);
    }
  };

  const handleVerifySubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    resetMessages();

    if (!verifyEmail.trim() || !verifyToken.trim()) {
      setError('Both email and verification code are required.');
      return;
    }

    setLoading(true);
    try {
      const res = await api.verifyEmail({
        email: verifyEmail.trim(),
        token: verifyToken.trim()
      });

      if (!res.ok) {
        setError(res.message || 'Verification failed. Please check the code.');
        return;
      }

      setSuccessMessage('Email verified successfully! You can now sign in.');
      setLoginEmail(verifyEmail.trim());
      setLoginPassword(regPassword || '');
      setTimeout(() => {
        setMode('login');
      }, 1200);
    } catch (err: any) {
      setError(err.message || 'Verification failed.');
    } finally {
      setLoading(false);
    }
  };

  const handleResendCode = async () => {
    if (!verifyEmail.trim()) {
      setError('Please enter your email to resend code.');
      return;
    }
    resetMessages();
    setLoading(true);
    try {
      const res = await api.resendVerification(verifyEmail.trim());
      if (res.verificationToken) {
        setDevToken(res.verificationToken);
        setVerifyToken(res.verificationToken);
      }
      setSuccessMessage('A fresh verification code was sent from admin@flowosbd.com.');
    } catch (err: any) {
      setError(err.message || 'Could not resend code.');
    } finally {
      setLoading(false);
    }
  };

  const handleAdminSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    resetMessages();

    const session: AuthSession = {
      role: 'Admin',
      tenantId: '11111111-1111-1111-1111-111111111111',
      tenantName: 'Platform Administrator',
      username: adminUsername || 'admin@flowos.internal',
      isSandbox: false,
      isEmailVerified: true
    };
    setAuthSession(session);
    onSuccess(session);
    onClose();
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-md animate-fadeIn">
      {/* Background glow */}
      <div className="absolute w-[500px] h-[300px] bg-gradient-to-tr from-blue-600/20 via-indigo-600/20 to-purple-600/10 blur-[100px] pointer-events-none rounded-full" />

      {/* Modal Card */}
      <div className="bg-slate-900 border border-slate-800 rounded-3xl max-w-lg w-full p-6 md:p-8 shadow-2xl relative z-10 space-y-5">
        
        {/* Close Button */}
        <button
          onClick={onClose}
          className="absolute top-5 right-5 w-8 h-8 rounded-full bg-slate-800 hover:bg-slate-700 text-slate-400 hover:text-white flex items-center justify-center transition-colors"
          title="Close modal"
        >
          <X size={16} />
        </button>

        {/* Modal Header & Navigation */}
        <div>
          <div className="flex items-center gap-2 mb-2">
            <span className="w-2 h-2 rounded-full bg-blue-500 animate-pulse" />
            <span className="text-[11px] font-bold uppercase tracking-wider text-blue-400">
              FlowOS Tenant Control Plane
            </span>
          </div>

          <h2 className="text-2xl font-extrabold text-white tracking-tight">
            {mode === 'login' && 'Sign In to FlowOS'}
            {mode === 'register' && 'Register New Tenant'}
            {mode === 'verify' && 'Verify Email Address'}
            {mode === 'admin' && 'Platform Governance Login'}
          </h2>
          <p className="text-xs text-slate-400 mt-1">
            {mode === 'login' && 'Access your isolated enterprise workspace and live workflow pipelines.'}
            {mode === 'register' && 'Provision an isolated multi-tenant environment with private API keys.'}
            {mode === 'verify' && 'Verification dispatched from official address: admin@flowosbd.com'}
            {mode === 'admin' && 'Root platform administrator access for fleet management and audits.'}
          </p>
        </div>

        {/* Top Mode Tabs */}
        {mode !== 'verify' && (
          <div className="grid grid-cols-3 p-1 bg-slate-950 rounded-xl border border-slate-800 text-xs font-semibold">
            <button
              onClick={() => { setMode('login'); resetMessages(); }}
              className={`py-2 px-3 rounded-lg flex items-center justify-center gap-1.5 transition-all ${
                mode === 'login' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
              }`}
            >
              <Building2 size={14} />
              <span>Sign In</span>
            </button>
            <button
              onClick={() => { setMode('register'); resetMessages(); }}
              className={`py-2 px-3 rounded-lg flex items-center justify-center gap-1.5 transition-all ${
                mode === 'register' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
              }`}
            >
              <Sparkles size={14} />
              <span>Register</span>
            </button>
            <button
              onClick={() => { setMode('admin'); resetMessages(); }}
              className={`py-2 px-3 rounded-lg flex items-center justify-center gap-1.5 transition-all ${
                mode === 'admin' ? 'bg-purple-600 text-white shadow' : 'text-slate-400 hover:text-white'
              }`}
            >
              <Shield size={14} />
              <span>Admin</span>
            </button>
          </div>
        )}

        {/* Status Alerts */}
        {error && (
          <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-300 flex items-start gap-2 animate-fadeIn">
            <AlertCircle size={16} className="mt-0.5 shrink-0 text-rose-400" />
            <div className="flex-1">{error}</div>
          </div>
        )}

        {successMessage && (
          <div className="p-3 bg-emerald-500/10 border border-emerald-500/20 rounded-xl text-xs text-emerald-300 flex items-start gap-2 animate-fadeIn">
            <CheckCircle2 size={16} className="mt-0.5 shrink-0 text-emerald-400" />
            <div className="flex-1">{successMessage}</div>
          </div>
        )}

        {/* MODE 1: LOGIN */}
        {mode === 'login' && (
          <form onSubmit={handleLoginSubmit} className="space-y-4">
            {/* Toggle credentials vs API key */}
            <div className="flex justify-between items-center text-xs">
              <span className="text-slate-400 font-medium">Authentication Method:</span>
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => setLoginType('credentials')}
                  className={`px-2.5 py-1 rounded-lg border text-[11px] ${
                    loginType === 'credentials'
                      ? 'bg-blue-500/20 text-blue-300 border-blue-500/40'
                      : 'bg-slate-800 text-slate-400 border-slate-700'
                  }`}
                >
                  Email & Password
                </button>
                <button
                  type="button"
                  onClick={() => setLoginType('apiKey')}
                  className={`px-2.5 py-1 rounded-lg border text-[11px] ${
                    loginType === 'apiKey'
                      ? 'bg-blue-500/20 text-blue-300 border-blue-500/40'
                      : 'bg-slate-800 text-slate-400 border-slate-700'
                  }`}
                >
                  API Key / UUID
                </button>
              </div>
            </div>

            {loginType === 'credentials' ? (
              <>
                <div className="space-y-1.5">
                  <label className="text-xs font-medium text-slate-300 flex items-center gap-1.5">
                    <Mail size={13} className="text-blue-400" />
                    <span>Business Email</span>
                  </label>
                  <input
                    type="email"
                    required
                    placeholder="admin@yourcompany.com"
                    value={loginEmail}
                    onChange={(e) => setLoginEmail(e.target.value)}
                    className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2.5 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div className="space-y-1.5">
                  <label className="text-xs font-medium text-slate-300 flex items-center justify-between">
                    <span className="flex items-center gap-1.5">
                      <Lock size={13} className="text-blue-400" />
                      <span>Password</span>
                    </span>
                    <button
                      type="button"
                      onClick={() => setShowPassword(!showPassword)}
                      className="text-[11px] text-slate-400 hover:text-slate-200 flex items-center gap-1"
                    >
                      {showPassword ? <EyeOff size={12} /> : <Eye size={12} />}
                      <span>{showPassword ? 'Hide' : 'Show'}</span>
                    </button>
                  </label>
                  <input
                    type={showPassword ? 'text' : 'password'}
                    required
                    placeholder="••••••••"
                    value={loginPassword}
                    onChange={(e) => setLoginPassword(e.target.value)}
                    className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2.5 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500"
                  />
                </div>
              </>
            ) : (
              <>
                <div className="space-y-1.5">
                  <label className="text-xs font-medium text-slate-300 flex items-center gap-1.5">
                    <Building2 size={13} className="text-blue-400" />
                    <span>Tenant UUID</span>
                  </label>
                  <input
                    type="text"
                    required
                    placeholder="22222222-2222-2222-2222-222222222222"
                    value={loginTenantId}
                    onChange={(e) => setLoginTenantId(e.target.value)}
                    className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2.5 text-xs text-white font-mono placeholder-slate-500 focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div className="space-y-1.5">
                  <label className="text-xs font-medium text-slate-300 flex items-center gap-1.5">
                    <Key size={13} className="text-amber-400" />
                    <span>Tenant API Key</span>
                  </label>
                  <input
                    type="text"
                    placeholder="flowos_prod_secret_key_32_chars_min"
                    value={loginApiKey}
                    onChange={(e) => setLoginApiKey(e.target.value)}
                    className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2.5 text-xs text-slate-200 font-mono placeholder-slate-500 focus:outline-none focus:border-blue-500"
                  />
                </div>
              </>
            )}

            <button
              type="submit"
              disabled={loading}
              className="w-full py-3 bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white font-semibold rounded-xl shadow-lg shadow-blue-500/25 transition-all flex items-center justify-center gap-2 text-sm mt-2"
            >
              {loading ? (
                <>
                  <RefreshCw size={16} className="animate-spin" />
                  <span>Authenticating...</span>
                </>
              ) : (
                <>
                  <span>Sign In to Tenant Workspace</span>
                  <ArrowRight size={16} />
                </>
              )}
            </button>

            {/* Quick Demo Sandbox Access */}
            <div className="pt-2 border-t border-slate-800 text-center">
              <p className="text-xs text-slate-400 mb-2">No account yet or evaluating FlowOS?</p>
              <button
                type="button"
                onClick={() => {
                  onClose();
                  onLaunchSandbox();
                }}
                className="w-full py-2.5 px-4 bg-slate-800 hover:bg-slate-750 border border-slate-700 hover:border-emerald-500/40 rounded-xl text-xs font-semibold text-emerald-300 flex items-center justify-center gap-2 transition-all"
              >
                <Sparkles size={14} className="text-emerald-400" />
                <span>Launch Demo Sandbox (Instant Guest Access, No Registration)</span>
              </button>
            </div>
          </form>
        )}

        {/* MODE 2: REGISTER */}
        {mode === 'register' && (
          <form onSubmit={handleRegisterSubmit} className="space-y-3.5">
            <div className="p-2.5 bg-blue-500/10 border border-blue-500/20 rounded-xl text-[11px] text-blue-300 flex items-start gap-2">
              <Mail size={15} className="text-blue-400 mt-0.5 shrink-0" />
              <span>
                Verification email with a 6-digit confirmation code will be dispatched from our official address: <strong>admin@flowosbd.com</strong>.
              </span>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-medium text-slate-300 flex items-center gap-1.5">
                <Building2 size={13} className="text-blue-400" />
                <span>Organization / Tenant Name</span>
              </label>
              <input
                type="text"
                required
                placeholder="e.g. Acme FinTech Corp"
                value={regOrgName}
                onChange={(e) => setRegOrgName(e.target.value)}
                className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500"
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-medium text-slate-300 flex items-center gap-1.5">
                <User size={13} className="text-blue-400" />
                <span>Administrator Full Name (Optional)</span>
              </label>
              <input
                type="text"
                placeholder="e.g. Alex Morgan"
                value={regFullName}
                onChange={(e) => setRegFullName(e.target.value)}
                className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500"
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-medium text-slate-300 flex items-center gap-1.5">
                <Mail size={13} className="text-blue-400" />
                <span>Administrator Email</span>
              </label>
              <input
                type="email"
                required
                placeholder="admin@acmefintech.com"
                value={regEmail}
                onChange={(e) => setRegEmail(e.target.value)}
                className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500"
              />
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1">
                <label className="text-xs font-medium text-slate-300 flex items-center gap-1.5">
                  <Lock size={13} className="text-blue-400" />
                  <span>Password (min 8)</span>
                </label>
                <input
                  type="password"
                  required
                  placeholder="••••••••"
                  value={regPassword}
                  onChange={(e) => setRegPassword(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500"
                />
              </div>
              <div className="space-y-1">
                <label className="text-xs font-medium text-slate-300 flex items-center gap-1.5">
                  <Lock size={13} className="text-blue-400" />
                  <span>Confirm Password</span>
                </label>
                <input
                  type="password"
                  required
                  placeholder="••••••••"
                  value={regConfirmPassword}
                  onChange={(e) => setRegConfirmPassword(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500"
                />
              </div>
            </div>

            <button
              type="submit"
              disabled={loading}
              className="w-full py-3 bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-500 hover:to-indigo-500 disabled:opacity-50 text-white font-semibold rounded-xl shadow-lg shadow-blue-500/25 transition-all flex items-center justify-center gap-2 text-sm mt-3"
            >
              {loading ? (
                <>
                  <RefreshCw size={16} className="animate-spin" />
                  <span>Registering Organization...</span>
                </>
              ) : (
                <>
                  <span>Create Tenant & Send Verification</span>
                  <ArrowRight size={16} />
                </>
              )}
            </button>
          </form>
        )}

        {/* MODE 3: VERIFY EMAIL */}
        {mode === 'verify' && (
          <form onSubmit={handleVerifySubmit} className="space-y-4">
            <div className="p-3 bg-slate-950 border border-slate-800 rounded-2xl space-y-1.5">
              <div className="text-xs text-slate-400">Verifying address:</div>
              <div className="text-sm font-semibold text-white flex items-center gap-2">
                <Mail size={14} className="text-blue-400" />
                <span>{verifyEmail}</span>
              </div>
              <div className="text-[11px] text-slate-500">
                Official sender: <strong className="text-slate-300">admin@flowosbd.com</strong>
              </div>
            </div>

            {devToken && (
              <div className="p-3 bg-emerald-500/10 border border-emerald-500/30 rounded-xl text-xs text-emerald-300">
                <div className="font-bold flex items-center justify-between">
                  <span>⚡ Development / Sandbox Fast-Verify:</span>
                  <button
                    type="button"
                    onClick={() => setVerifyToken(devToken)}
                    className="text-[10px] px-2 py-0.5 bg-emerald-500/20 hover:bg-emerald-500/30 text-emerald-200 rounded border border-emerald-500/40"
                  >
                    Auto-Fill Code
                  </button>
                </div>
                <div className="font-mono text-sm tracking-widest text-white mt-1">
                  {devToken}
                </div>
              </div>
            )}

            <div className="space-y-1.5">
              <label className="text-xs font-medium text-slate-300">Enter 6-Digit Verification Code</label>
              <input
                type="text"
                required
                maxLength={8}
                placeholder="123456"
                value={verifyToken}
                onChange={(e) => setVerifyToken(e.target.value.toUpperCase())}
                className="w-full bg-slate-950 border border-slate-700 rounded-xl px-4 py-3 text-center text-lg font-mono tracking-widest text-white focus:outline-none focus:border-blue-500"
              />
            </div>

            <button
              type="submit"
              disabled={loading}
              className="w-full py-3 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-semibold rounded-xl shadow-lg shadow-emerald-500/25 transition-all flex items-center justify-center gap-2 text-sm"
            >
              {loading ? (
                <>
                  <RefreshCw size={16} className="animate-spin" />
                  <span>Verifying Code...</span>
                </>
              ) : (
                <>
                  <CheckCircle2 size={16} />
                  <span>Confirm Email & Activate Tenant</span>
                </>
              )}
            </button>

            <div className="flex justify-between items-center text-xs text-slate-400 pt-2 border-t border-slate-800">
              <button
                type="button"
                onClick={handleResendCode}
                disabled={loading}
                className="text-blue-400 hover:underline flex items-center gap-1"
              >
                <RefreshCw size={12} />
                <span>Resend Code</span>
              </button>

              <button
                type="button"
                onClick={() => { setMode('login'); resetMessages(); }}
                className="text-slate-400 hover:text-white"
              >
                Back to Sign In
              </button>
            </div>
          </form>
        )}

        {/* MODE 4: PLATFORM ADMIN */}
        {mode === 'admin' && (
          <form onSubmit={handleAdminSubmit} className="space-y-4">
            <div className="p-3 bg-purple-500/10 border border-purple-500/20 rounded-xl text-xs text-purple-300 flex items-start gap-2">
              <Shield size={16} className="text-purple-400 mt-0.5 shrink-0" />
              <span>
                Platform governance console for cluster administrators, multi-tenant fleet health, and immutable audit logs.
              </span>
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-medium text-slate-300">Admin Account</label>
              <input
                type="text"
                required
                value={adminUsername}
                onChange={(e) => setAdminUsername(e.target.value)}
                className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2.5 text-xs text-white focus:outline-none focus:border-purple-500"
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-medium text-slate-300">Root Password</label>
              <input
                type="password"
                required
                value={adminPassword}
                onChange={(e) => setAdminPassword(e.target.value)}
                className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3.5 py-2.5 text-xs text-white font-mono focus:outline-none focus:border-purple-500"
              />
            </div>

            <button
              type="submit"
              className="w-full py-3 bg-purple-600 hover:bg-purple-500 text-white font-semibold rounded-xl shadow-lg shadow-purple-500/25 transition-all flex items-center justify-center gap-2 text-sm mt-2"
            >
              <Shield size={16} />
              <span>Enter Platform Governance Console</span>
            </button>
          </form>
        )}

      </div>
    </div>
  );
};
