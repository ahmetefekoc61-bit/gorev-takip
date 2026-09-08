import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export type UserRole = 'Admin' | 'TeamLeader' | 'TeamMember';

export interface UserSummary {
  id: number;
  fullName: string;
  email: string;
  role: UserRole;
  teamId: number | null;
  avatarUrl: string | null;
}

// Backend'in kendi origin'i - yüklenen fotoğraflar "/avatars/xxx.jpg" gibi göreli bir
// yol olarak dönüyor, <img> ile göstermek için başına bunu ekliyoruz.
export const API_ORIGIN = 'https://localhost:7236';

/**
 * Bir kullanıcının AvatarUrl'sini <img src> için kullanılabilir tam bir adrese çevirir.
 * Ünlülerin fotoğrafları gibi zaten tam https:// adresi olanlara dokunmaz, sadece
 * bizim sunucumuzdan yüklenen göreli yollara ("/avatars/...") origin ekler.
 */
export function resolveAvatarUrl(avatarUrl: string | null | undefined): string | null {
  if (!avatarUrl) return null;
  return avatarUrl.startsWith('http') ? avatarUrl : `${API_ORIGIN}${avatarUrl}`;
}

/**
 * Görev atama (assignee) dropdown'ı ve ekip yönetimi sayfası için kullanıcı listesini getirir.
 * Ayrı isimle (UserDirectoryService) tanımlandı ki AuthService'teki "kullanıcı"
 * kavramıyla karışmasın.
 */
@Injectable({
  providedIn: 'root'
})
export class UserDirectoryService {
  private apiUrl = 'https://localhost:7236/api/users';

  constructor(private http: HttpClient) { }

  getUsers(): Observable<UserSummary[]> {
    return this.http.get<UserSummary[]>(this.apiUrl);
  }

  /**
   * Bir kullanıcının rolünü ve/veya ekibini değiştirir. Sadece yönetici çağırabilir
   * (backend UsersController.UpdateUserRole bunu zorunlu kılıyor).
   */
  updateUserRole(id: number, role: UserRole, teamId: number | null): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${id}/role`, { role, teamId });
  }

  /**
   * Profil fotoğrafı yükler. Admin herkes için, normal kullanıcı sadece kendi
   * hesabı için çağırabilir (backend bunu zorunlu kılıyor).
   */
  uploadAvatar(id: number, file: File): Observable<{ avatarUrl: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ avatarUrl: string }>(`${this.apiUrl}/${id}/avatar`, formData);
  }
}
