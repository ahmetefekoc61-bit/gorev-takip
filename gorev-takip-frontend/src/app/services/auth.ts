import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { SearchService } from './search';

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  token: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ForgotPasswordResponse {
  message: string;
}

export interface ResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}

export type UserRole = 'Admin' | 'TeamLeader' | 'TeamMember';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = 'https://localhost:7236/api/auth';

  // Token her değiştiğinde (login/logout) sıfırlanan basit bir cache;
  // aynı token için JWT payload'ı defalarca decode etmeyelim diye.
  private cachedToken: string | null = null;
  private cachedPayload: Record<string, any> | null = null;

  private readonly searchService = inject(SearchService);

  constructor(private http: HttpClient) { }

  /**
   * "Beni hatırla" işaretliyse token localStorage'a (tarayıcı kapansa da kalır),
   * değilse sessionStorage'a (sekme kapanınca silinir) yazılıyor. Önceden bu seçim
   * hiç okunmuyordu ve token her koşulda kalıcı olarak saklanıyordu.
   */
  login(credentials: LoginRequest, rememberMe = true): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/login`, credentials).pipe(
      tap(response => {
        this.clearStoredToken();
        (rememberMe ? localStorage : sessionStorage).setItem('token', response.token);
        this.cachedToken = null;
        this.cachedPayload = null;
      })
    );
  }

  logout(): void {
    this.clearStoredToken();
    this.cachedToken = null;
    this.cachedPayload = null;

    // Arama terimi uygulama genelinde paylaşıldığı için oturum kapanınca sıfırlanmalı;
    // aksi halde bir sonraki girişte arama kutusu boş görünmesine rağmen listeler
    // filtreli kalıyor ve kayıtlar kaybolmuş gibi duruyordu.
    this.searchService.clear();
  }

  private clearStoredToken(): void {
    localStorage.removeItem('token');
    sessionStorage.removeItem('token');
  }

  getToken(): string | null {
    return localStorage.getItem('token') ?? sessionStorage.getItem('token');
  }

  isLoggedIn(): boolean {
    const token = this.getToken();
    if (!token) return false;
    // Süresi dolmuş token varsa oturum açık sayılmasın; sunucuya boşuna
    // 401 alacak istek atmak yerine burada yakalayalım.
    const payload = this.decodePayload(token);
    const exp = payload?.['exp'];
    if (typeof exp === 'number' && Date.now() >= exp * 1000) {
      return false;
    }
    return true;
  }

  /**
   * Backend'in ürettiği JWT'nin (AuthController.GenerateJwtToken) içindeki
   * claim'leri okur. JwtSecurityTokenHandler, ClaimTypes.* değerlerini yazarken
   * kısa isimlere eşler (nameid/role/email), o yüzden hem kısa hem uzun (ClaimTypes)
   * anahtarları destekliyoruz.
   */
  private decodePayload(token: string): Record<string, any> | null {
    if (this.cachedToken === token && this.cachedPayload) {
      return this.cachedPayload;
    }
    try {
      const base64Url = token.split('.')[1];
      if (!base64Url) return null;
      const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
      const padded = base64.padEnd(base64.length + (4 - (base64.length % 4)) % 4, '=');
      const json = decodeURIComponent(
        atob(padded)
          .split('')
          .map(c => '%' + c.charCodeAt(0).toString(16).padStart(2, '0'))
          .join('')
      );
      const payload = JSON.parse(json);
      this.cachedToken = token;
      this.cachedPayload = payload;
      return payload;
    } catch {
      return null;
    }
  }

  private getClaim(...keys: string[]): string | null {
    const token = this.getToken();
    if (!token) return null;
    const payload = this.decodePayload(token);
    if (!payload) return null;
    for (const key of keys) {
      if (payload[key] !== undefined && payload[key] !== null) {
        return String(payload[key]);
      }
    }
    return null;
  }

  getUserId(): number | null {
    const raw = this.getClaim(
      'nameid',
      'sub',
      'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'
    );
    return raw !== null ? Number(raw) : null;
  }

  getRole(): UserRole | null {
    const raw = this.getClaim(
      'role',
      'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'
    );
    return (raw as UserRole) ?? null;
  }

  getTeamId(): number | null {
    const raw = this.getClaim('TeamId');
    return raw !== null ? Number(raw) : null;
  }

  isAdmin(): boolean {
    return this.getRole() === 'Admin';
  }

  changePassword(request: ChangePasswordRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/change-password`, request);
  }

  forgotPassword(request: ForgotPasswordRequest): Observable<ForgotPasswordResponse> {
    return this.http.post<ForgotPasswordResponse>(`${this.apiUrl}/forgot-password`, request);
  }

  resetPassword(request: ResetPasswordRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/reset-password`, request);
  }
}